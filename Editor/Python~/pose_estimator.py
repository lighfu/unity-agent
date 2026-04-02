"""
動画から姿勢推定を行い、Unity Humanoid 互換の JSON を出力する。

Usage:
    python pose_estimator.py --input video.mp4 --output pose.json [--fps 30]
"""

import argparse
import json
import os
import sys
import math
import urllib.request

import cv2
import numpy as np
import mediapipe as mp
from mediapipe.tasks.python import BaseOptions
from mediapipe.tasks.python.vision import (
    PoseLandmarker,
    PoseLandmarkerOptions,
    RunningMode,
)

# ── モデルファイル ──
# MediaPipe の C ライブラリは非 ASCII パスを扱えないため、
# ユーザーの LOCALAPPDATA 配下に保存する
MODEL_URL = ("https://storage.googleapis.com/mediapipe-models/"
             "pose_landmarker/pose_landmarker_heavy/float16/latest/"
             "pose_landmarker_heavy.task")
MODEL_CACHE_DIR = os.path.join(
    os.environ.get("LOCALAPPDATA", os.path.expanduser("~")),
    "AjisaiFlow", "mediapipe_models")
MODEL_PATH = os.path.join(MODEL_CACHE_DIR, "pose_landmarker_heavy.task")

# ── MediaPipe ランドマークインデックス ──
LM_NOSE = 0
LM_LEFT_SHOULDER = 11
LM_RIGHT_SHOULDER = 12
LM_LEFT_ELBOW = 13
LM_RIGHT_ELBOW = 14
LM_LEFT_WRIST = 15
LM_RIGHT_WRIST = 16
LM_LEFT_HIP = 23
LM_RIGHT_HIP = 24
LM_LEFT_KNEE = 25
LM_RIGHT_KNEE = 26
LM_LEFT_ANKLE = 27
LM_RIGHT_ANKLE = 28

# ── ボーン定義: (名前, 親ランドマーク群, 子ランドマーク群) ──
BONE_DEFINITIONS = [
    ("Hips", None, None),
    ("Spine", [LM_LEFT_HIP, LM_RIGHT_HIP], [LM_LEFT_SHOULDER, LM_RIGHT_SHOULDER]),
    ("Chest", [LM_LEFT_SHOULDER, LM_RIGHT_SHOULDER], None),
    ("Head", [LM_LEFT_SHOULDER, LM_RIGHT_SHOULDER], [LM_NOSE]),
    ("LeftUpperArm", [LM_LEFT_SHOULDER], [LM_LEFT_ELBOW]),
    ("LeftLowerArm", [LM_LEFT_ELBOW], [LM_LEFT_WRIST]),
    ("RightUpperArm", [LM_RIGHT_SHOULDER], [LM_RIGHT_ELBOW]),
    ("RightLowerArm", [LM_RIGHT_ELBOW], [LM_RIGHT_WRIST]),
    ("LeftUpperLeg", [LM_LEFT_HIP], [LM_LEFT_KNEE]),
    ("LeftLowerLeg", [LM_LEFT_KNEE], [LM_LEFT_ANKLE]),
    ("RightUpperLeg", [LM_RIGHT_HIP], [LM_RIGHT_KNEE]),
    ("RightLowerLeg", [LM_RIGHT_KNEE], [LM_RIGHT_ANKLE]),
]

# ── ボーン親子関係 (local 回転の算出に使用) ──
BONE_PARENTS = {
    "Hips": None,
    "Spine": "Hips",
    "Chest": "Spine",
    "Head": "Chest",
    "LeftUpperArm": "Chest",
    "LeftLowerArm": "LeftUpperArm",
    "RightUpperArm": "Chest",
    "RightLowerArm": "RightUpperArm",
    "LeftUpperLeg": "Hips",
    "LeftLowerLeg": "LeftUpperLeg",
    "RightUpperLeg": "Hips",
    "RightLowerLeg": "RightUpperLeg",
}

# ── T-ポーズ時の各ボーンの基準方向 (Unity 座標系) ──
# MediaPipe pose_world_landmarks: X+=被写体の左, Y+=下, Z+=被写体の前
# mediapipe_to_unity 変換後: X+=被写体の左, Y+=上, Z+=被写体の後ろ
REST_DIRECTIONS = {
    "Spine": np.array([0, 1, 0], dtype=np.float64),
    "Chest": np.array([0, 1, 0], dtype=np.float64),
    "Head": np.array([0, 1, 0], dtype=np.float64),
    "LeftUpperArm": np.array([1, 0, 0], dtype=np.float64),
    "LeftLowerArm": np.array([1, 0, 0], dtype=np.float64),
    "RightUpperArm": np.array([-1, 0, 0], dtype=np.float64),
    "RightLowerArm": np.array([-1, 0, 0], dtype=np.float64),
    "LeftUpperLeg": np.array([0, -1, 0], dtype=np.float64),
    "LeftLowerLeg": np.array([0, -1, 0], dtype=np.float64),
    "RightUpperLeg": np.array([0, -1, 0], dtype=np.float64),
    "RightLowerLeg": np.array([0, -1, 0], dtype=np.float64),
}

IDENTITY_QUAT = [0.0, 0.0, 0.0, 1.0]


# ================================================================
# 座標・ベクトルユーティリティ
# ================================================================

def mediapipe_to_unity(x, y, z):
    """MediaPipe 座標系 → Unity 座標系 (左手系, Y-up)。"""
    return np.array([x, -y, -z], dtype=np.float64)


def get_landmark_pos(landmarks, idx):
    lm = landmarks[idx]
    return mediapipe_to_unity(lm.x, lm.y, lm.z)


def midpoint(landmarks, indices):
    pts = [get_landmark_pos(landmarks, i) for i in indices]
    return np.mean(pts, axis=0)


def normalize(v):
    length = np.linalg.norm(v)
    if length < 1e-8:
        return np.array([0, 1, 0], dtype=np.float64)
    return v / length


# ================================================================
# クォータニオン演算 [x, y, z, w] 形式
# ================================================================

def quat_multiply(a, b):
    """クォータニオン乗算: a * b"""
    ax, ay, az, aw = a
    bx, by, bz, bw = b
    return [
        aw * bx + ax * bw + ay * bz - az * by,
        aw * by - ax * bz + ay * bw + az * bx,
        aw * bz + ax * by - ay * bx + az * bw,
        aw * bw - ax * bx - ay * by - az * bz,
    ]


def quat_inverse(q):
    """単位クォータニオンの逆 (共役)。"""
    return [-q[0], -q[1], -q[2], q[3]]


def quat_normalize(q):
    """クォータニオンを正規化。"""
    n = math.sqrt(q[0]**2 + q[1]**2 + q[2]**2 + q[3]**2)
    if n < 1e-10:
        return list(IDENTITY_QUAT)
    return [q[0]/n, q[1]/n, q[2]/n, q[3]/n]


def quaternion_from_to(v_from, v_to):
    """v_from → v_to への回転クォータニオン [x, y, z, w]。"""
    v_from = normalize(v_from)
    v_to = normalize(v_to)
    dot = float(np.clip(np.dot(v_from, v_to), -1.0, 1.0))

    if dot > 0.999999:
        return list(IDENTITY_QUAT)

    if dot < -0.999999:
        ortho = np.array([1, 0, 0], dtype=np.float64)
        if abs(np.dot(v_from, ortho)) > 0.9:
            ortho = np.array([0, 1, 0], dtype=np.float64)
        axis = normalize(np.cross(v_from, ortho))
        return [float(axis[0]), float(axis[1]), float(axis[2]), 0.0]

    axis = np.cross(v_from, v_to)
    w = 1.0 + dot
    q = [float(axis[0]), float(axis[1]), float(axis[2]), w]
    return quat_normalize(q)


def quaternion_from_matrix(m):
    """3x3 回転行列 → クォータニオン [x, y, z, w]。"""
    trace = m[0, 0] + m[1, 1] + m[2, 2]
    if trace > 0:
        s = 0.5 / math.sqrt(trace + 1.0)
        w = 0.25 / s
        x = (m[2, 1] - m[1, 2]) * s
        y = (m[0, 2] - m[2, 0]) * s
        z = (m[1, 0] - m[0, 1]) * s
    elif m[0, 0] > m[1, 1] and m[0, 0] > m[2, 2]:
        s = 2.0 * math.sqrt(1.0 + m[0, 0] - m[1, 1] - m[2, 2])
        w = (m[2, 1] - m[1, 2]) / s
        x = 0.25 * s
        y = (m[0, 1] + m[1, 0]) / s
        z = (m[0, 2] + m[2, 0]) / s
    elif m[1, 1] > m[2, 2]:
        s = 2.0 * math.sqrt(1.0 + m[1, 1] - m[0, 0] - m[2, 2])
        w = (m[0, 2] - m[2, 0]) / s
        x = (m[0, 1] + m[1, 0]) / s
        y = 0.25 * s
        z = (m[1, 2] + m[2, 1]) / s
    else:
        s = 2.0 * math.sqrt(1.0 + m[2, 2] - m[0, 0] - m[1, 1])
        w = (m[1, 0] - m[0, 1]) / s
        x = (m[0, 2] + m[2, 0]) / s
        y = (m[1, 2] + m[2, 1]) / s
        z = 0.25 * s
    return quat_normalize([x, y, z, w])


# ================================================================
# ボーンワールド回転の計算
# ================================================================

def compute_hips_world(landmarks):
    """Hips のワールド回転と位置を計算。"""
    left_hip = get_landmark_pos(landmarks, LM_LEFT_HIP)
    right_hip = get_landmark_pos(landmarks, LM_RIGHT_HIP)
    position = (left_hip + right_hip) / 2.0

    hip_right = normalize(right_hip - left_hip)
    spine_mid = midpoint(landmarks, [LM_LEFT_SHOULDER, LM_RIGHT_SHOULDER])
    up = normalize(spine_mid - position)
    forward = normalize(np.cross(up, hip_right))
    right = normalize(np.cross(forward, up))

    rot_matrix = np.column_stack([right, up, forward])
    return position, quaternion_from_matrix(rot_matrix)


def compute_bone_world_rotation(landmarks, bone_name, parent_indices,
                                child_indices):
    """ボーンのワールド回転を計算 (rest → current)。"""
    if parent_indices is None or child_indices is None:
        return None

    parent_pos = midpoint(landmarks, parent_indices)
    child_pos = midpoint(landmarks, child_indices)
    direction = normalize(child_pos - parent_pos)

    rest_dir = REST_DIRECTIONS.get(bone_name)
    if rest_dir is None:
        return None

    return quaternion_from_to(rest_dir, direction)


def compute_chest_world_rotation(landmarks):
    """Chest のワールド回転 (Spine と差別化: 肩の左右方向も考慮)。"""
    left_shoulder = get_landmark_pos(landmarks, LM_LEFT_SHOULDER)
    right_shoulder = get_landmark_pos(landmarks, LM_RIGHT_SHOULDER)
    shoulder_mid = (left_shoulder + right_shoulder) / 2.0
    hip_mid = midpoint(landmarks, [LM_LEFT_HIP, LM_RIGHT_HIP])

    up = normalize(shoulder_mid - hip_mid)
    shoulder_right = normalize(right_shoulder - left_shoulder)
    forward = normalize(np.cross(up, shoulder_right))
    right = normalize(np.cross(forward, up))

    rot_matrix = np.column_stack([right, up, forward])
    return quaternion_from_matrix(rot_matrix)


# ================================================================
# フレーム処理: world → local 変換
# ================================================================

def process_frame(landmarks):
    """1フレーム分のランドマークからローカルボーン回転を生成。

    1. 全ボーンのワールド回転を算出
    2. 親子関係から local = inv(parent_world) * child_world を計算
    """
    world_rotations = {}

    # Step 1: ワールド回転を計算
    hips_pos = None
    for bone_name, parent_indices, child_indices in BONE_DEFINITIONS:
        if bone_name == "Hips":
            pos, rot = compute_hips_world(landmarks)
            hips_pos = pos
            world_rotations["Hips"] = rot
        elif bone_name == "Chest":
            world_rotations["Chest"] = compute_chest_world_rotation(landmarks)
        else:
            rot = compute_bone_world_rotation(landmarks, bone_name,
                                              parent_indices, child_indices)
            if rot is not None:
                world_rotations[bone_name] = rot

    # Step 2: ワールド回転 → ローカル回転に変換
    bones = {}
    for bone_name, world_rot in world_rotations.items():
        parent_name = BONE_PARENTS.get(bone_name)

        if parent_name is None or parent_name not in world_rotations:
            # ルートボーン: ローカル = ワールド
            local_rot = world_rot
        else:
            # local = inv(parent_world) * child_world
            parent_world = world_rotations[parent_name]
            local_rot = quat_multiply(quat_inverse(parent_world), world_rot)
            local_rot = quat_normalize(local_rot)

        entry = {
            "rotation": [round(local_rot[0], 5),
                          round(local_rot[1], 5),
                          round(local_rot[2], 5),
                          round(local_rot[3], 5)]
        }

        if bone_name == "Hips" and hips_pos is not None:
            entry["position"] = [round(float(hips_pos[0]), 5),
                                  round(float(hips_pos[1]), 5),
                                  round(float(hips_pos[2]), 5)]

        bones[bone_name] = entry

    return bones


# ================================================================
# モデルダウンロード
# ================================================================

def ensure_model():
    if os.path.exists(MODEL_PATH):
        return
    os.makedirs(MODEL_CACHE_DIR, exist_ok=True)
    print(f"Downloading pose landmarker model...", file=sys.stderr)
    print(f"  URL: {MODEL_URL}", file=sys.stderr)
    print(f"  Dest: {MODEL_PATH}", file=sys.stderr)
    urllib.request.urlretrieve(MODEL_URL, MODEL_PATH)
    print(f"  Done.", file=sys.stderr)


# ================================================================
# 動画処理メインループ
# ================================================================

def process_video(input_path, output_path, target_fps=None):
    cap = cv2.VideoCapture(input_path)
    if not cap.isOpened():
        print(f"Error: Cannot open video file: {input_path}", file=sys.stderr)
        sys.exit(1)

    video_fps = cap.get(cv2.CAP_PROP_FPS)
    total_frames = int(cap.get(cv2.CAP_PROP_FRAME_COUNT))

    if target_fps is None or target_fps <= 0:
        target_fps = video_fps

    frame_interval = max(1, round(video_fps / target_fps))
    actual_fps = video_fps / frame_interval

    print(f"Video: {input_path}", file=sys.stderr)
    print(f"  Resolution: {int(cap.get(cv2.CAP_PROP_FRAME_WIDTH))}x"
          f"{int(cap.get(cv2.CAP_PROP_FRAME_HEIGHT))}", file=sys.stderr)
    print(f"  Video FPS: {video_fps:.1f}", file=sys.stderr)
    print(f"  Target FPS: {actual_fps:.1f} (interval: {frame_interval})",
          file=sys.stderr)
    print(f"  Total frames: {total_frames}", file=sys.stderr)

    ensure_model()
    print(f"  Model: {MODEL_PATH} ({os.path.getsize(MODEL_PATH)} bytes)",
          file=sys.stderr)

    options = PoseLandmarkerOptions(
        base_options=BaseOptions(model_asset_path=MODEL_PATH),
        running_mode=RunningMode.VIDEO,
        num_poses=1,
        min_pose_detection_confidence=0.3,
        min_pose_presence_confidence=0.3,
        min_tracking_confidence=0.3,
    )
    landmarker = PoseLandmarker.create_from_options(options)

    frames = []
    frame_idx = 0
    processed_count = 0
    detected_count = 0

    while True:
        ret, frame = cap.read()
        if not ret:
            break

        if frame_idx % frame_interval == 0:
            processed_count += 1
            rgb = cv2.cvtColor(frame, cv2.COLOR_BGR2RGB)
            mp_image = mp.Image(image_format=mp.ImageFormat.SRGB, data=rgb)
            timestamp_ms = int(frame_idx * 1000 / video_fps)

            result = landmarker.detect_for_video(mp_image, timestamp_ms)

            has_world = (result.pose_world_landmarks
                         and len(result.pose_world_landmarks) > 0)

            if processed_count <= 3:
                has_norm = (result.pose_landmarks
                            and len(result.pose_landmarks) > 0)
                print(f"  Frame {frame_idx}: world={has_world}, "
                      f"norm={has_norm}", file=sys.stderr)

            if has_world:
                detected_count += 1
                landmarks = result.pose_world_landmarks[0]
                time_sec = frame_idx / video_fps
                bones = process_frame(landmarks)
                frames.append({
                    "time": round(time_sec, 5),
                    "bones": bones
                })

            progress = frame_idx / max(total_frames, 1)
            print(f"PROGRESS:{progress:.3f}", file=sys.stderr, flush=True)

        frame_idx += 1

    cap.release()
    landmarker.close()

    print(f"  Processed: {processed_count}, Detected: {detected_count}",
          file=sys.stderr)

    output_data = {
        "fps": round(actual_fps, 2),
        "frameCount": len(frames),
        "frames": frames
    }

    with open(output_path, "w", encoding="utf-8") as f:
        json.dump(output_data, f, ensure_ascii=False)

    print(f"Done: {len(frames)} frames written to {output_path}",
          file=sys.stderr)
    print(f"PROGRESS:1.000", file=sys.stderr, flush=True)


def main():
    parser = argparse.ArgumentParser(
        description="動画から姿勢推定を行い JSON を出力する")
    parser.add_argument("--input", "-i", required=True,
                        help="入力動画ファイルパス")
    parser.add_argument("--output", "-o", required=True,
                        help="出力 JSON ファイルパス")
    parser.add_argument("--fps", type=float, default=0,
                        help="出力 FPS (0=元の動画FPSを使用)")
    args = parser.parse_args()

    process_video(args.input, args.output, args.fps if args.fps > 0 else None)


if __name__ == "__main__":
    main()
