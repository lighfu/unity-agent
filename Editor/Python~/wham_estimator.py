"""
WHAM を使用して動画から姿勢推定を行い、Unity Humanoid 互換の JSON を出力する。

前提条件:
  - WHAM リポジトリがクローンされ、環境がセットアップ済み
  - conda activate wham 等で WHAM 環境がアクティブ

Usage:
    python wham_estimator.py --wham-dir /path/to/WHAM --input video.mp4 --output pose.json [--fps 30]
"""

import argparse
import json
import math
import os
import sys


# ── SMPL 24 関節 → Unity HumanBodyBones マッピング ──
SMPL_TO_UNITY = {
    0: "Hips",
    1: "LeftUpperLeg",
    2: "RightUpperLeg",
    3: "Spine",
    4: "LeftLowerLeg",
    5: "RightLowerLeg",
    6: "Chest",
    7: "LeftFoot",
    8: "RightFoot",
    9: "UpperChest",
    10: "LeftToes",
    11: "RightToes",
    12: "Neck",
    13: "LeftShoulder",
    14: "RightShoulder",
    15: "Head",
    16: "LeftUpperArm",
    17: "RightUpperArm",
    18: "LeftLowerArm",
    19: "RightLowerArm",
    20: "LeftHand",
    21: "RightHand",
}


# ================================================================
# 数学ユーティリティ
# ================================================================

def axis_angle_to_quaternion(aa):
    """Axis-angle (3,) → quaternion [x, y, z, w]."""
    angle = math.sqrt(aa[0] ** 2 + aa[1] ** 2 + aa[2] ** 2)
    if angle < 1e-8:
        return [0.0, 0.0, 0.0, 1.0]
    half = angle / 2.0
    s = math.sin(half) / angle
    return [aa[0] * s, aa[1] * s, aa[2] * s, math.cos(half)]


def smpl_quat_to_unity(q):
    """SMPL 座標系 (右手系, Y-up) → Unity 座標系 (左手系, Y-up)。
    Z 軸を反転: qx, qy を反転、qz, qw はそのまま。"""
    return [-q[0], -q[1], q[2], q[3]]


def smpl_pos_to_unity(pos):
    """SMPL 位置 → Unity 位置。Z 軸を反転。"""
    return [float(pos[0]), float(pos[1]), float(-pos[2])]


# ================================================================
# WHAM 結果 → JSON 変換
# ================================================================

def process_wham_results(results, video_fps, target_fps):
    """WHAM の出力を Unity 互換 JSON 形式に変換。"""
    person_ids = list(results.keys())
    if not person_ids:
        print("Error: No person detected in video", file=sys.stderr)
        sys.exit(1)

    person = results[person_ids[0]]
    print(f"Result keys: {list(person.keys())}", file=sys.stderr)

    # WHAM 出力形式を自動検出
    if 'poses_body' in person:
        # WHAM_API 形式
        poses_body = person['poses_body']       # (N, 69)
        poses_root = person.get('poses_root_world',
                                person.get('poses_root_cam'))  # (N, 3)
        trans = person.get('trans_world', None)  # (N, 3)
        frame_ids = person.get('frame_id', None)
    elif 'pose_world' in person:
        # demo.py 形式: (N, 72) = root(3) + body(69)
        pose_world = person['pose_world']
        poses_root = pose_world[:, :3]
        poses_body = pose_world[:, 3:]
        trans = person.get('trans_world', None)
        frame_ids = person.get('frame_ids', None)
    elif 'pose' in person:
        # 別形式: (N, 72)
        pose = person['pose']
        poses_root = pose[:, :3]
        poses_body = pose[:, 3:]
        trans = person.get('trans', None)
        frame_ids = person.get('frame_ids', None)
    else:
        print(f"Error: Unknown result format. Keys: {list(person.keys())}",
              file=sys.stderr)
        sys.exit(1)

    num_frames = len(poses_body)
    print(f"Total frames from WHAM: {num_frames}", file=sys.stderr)

    # FPS 調整
    if target_fps and 0 < target_fps < video_fps:
        frame_interval = max(1, round(video_fps / target_fps))
    else:
        frame_interval = 1
    actual_fps = video_fps / frame_interval

    frames = []
    for i in range(0, num_frames, frame_interval):
        # Root rotation (joint 0)
        root_aa = poses_root[i]
        root_quat = axis_angle_to_quaternion(root_aa)
        root_quat_unity = smpl_quat_to_unity(root_quat)

        bones = {}

        # Hips (root)
        hips_entry = {
            "rotation": [round(root_quat_unity[0], 6),
                         round(root_quat_unity[1], 6),
                         round(root_quat_unity[2], 6),
                         round(root_quat_unity[3], 6)]
        }
        if trans is not None:
            pos = smpl_pos_to_unity(trans[i])
            hips_entry["position"] = [round(pos[0], 6),
                                      round(pos[1], 6),
                                      round(pos[2], 6)]
        bones["Hips"] = hips_entry

        # Body joints (1-23)
        body_pose = poses_body[i]  # (69,)
        for joint_idx in range(1, 24):
            if joint_idx not in SMPL_TO_UNITY:
                continue
            bone_name = SMPL_TO_UNITY[joint_idx]
            aa = body_pose[(joint_idx - 1) * 3: joint_idx * 3]
            q = axis_angle_to_quaternion(aa)
            q_unity = smpl_quat_to_unity(q)
            bones[bone_name] = {
                "rotation": [round(q_unity[0], 6),
                             round(q_unity[1], 6),
                             round(q_unity[2], 6),
                             round(q_unity[3], 6)]
            }

        # タイムスタンプ
        if frame_ids is not None:
            time_sec = float(frame_ids[i]) / video_fps
        else:
            time_sec = i / video_fps

        frames.append({
            "time": round(time_sec, 5),
            "bones": bones
        })

        progress = (i + 1) / num_frames
        print(f"PROGRESS:{progress:.3f}", file=sys.stderr, flush=True)

    return {
        "fps": round(actual_fps, 2),
        "frameCount": len(frames),
        "frames": frames
    }


# ================================================================
# メイン
# ================================================================

def main():
    parser = argparse.ArgumentParser(
        description="WHAM を使用して動画から姿勢推定を行い JSON を出力する")
    parser.add_argument("--wham-dir", required=True,
                        help="WHAM リポジトリのパス")
    parser.add_argument("--input", "-i", required=True,
                        help="入力動画ファイルパス")
    parser.add_argument("--output", "-o", required=True,
                        help="出力 JSON ファイルパス")
    parser.add_argument("--fps", type=float, default=0,
                        help="出力 FPS (0=元の動画FPSを使用)")
    args = parser.parse_args()

    # WHAM ディレクトリをパスに追加
    wham_dir = os.path.abspath(args.wham_dir)
    if not os.path.isdir(wham_dir):
        print(f"Error: WHAM directory not found: {wham_dir}", file=sys.stderr)
        sys.exit(1)

    sys.path.insert(0, wham_dir)
    original_cwd = os.getcwd()
    os.chdir(wham_dir)  # WHAM はリポジトリルートからの相対パスを使用

    # WHAM をインポート
    try:
        from wham_api import WHAM_API
    except ImportError as e:
        print(f"Error: Cannot import WHAM_API. "
              f"Make sure WHAM is properly installed.\n{e}",
              file=sys.stderr)
        sys.exit(1)

    print(f"WHAM loaded from: {wham_dir}", file=sys.stderr)
    print(f"Processing video: {args.input}", file=sys.stderr)

    # 動画 FPS を取得
    import cv2
    cap = cv2.VideoCapture(args.input)
    if not cap.isOpened():
        print(f"Error: Cannot open video: {args.input}", file=sys.stderr)
        sys.exit(1)
    video_fps = cap.get(cv2.CAP_PROP_FPS)
    total_frames = int(cap.get(cv2.CAP_PROP_FRAME_COUNT))
    cap.release()

    print(f"Video FPS: {video_fps:.1f}, Total frames: {total_frames}",
          file=sys.stderr)

    # WHAM 推論実行
    print("Running WHAM inference...", file=sys.stderr)
    print("PROGRESS:0.000", file=sys.stderr, flush=True)

    wham_model = WHAM_API()
    input_path = os.path.abspath(args.input) if not os.path.isabs(args.input) \
        else args.input
    results, tracking_results, slam_results = wham_model(input_path)

    print("WHAM inference complete. Converting results...", file=sys.stderr)

    # 結果を変換
    target_fps = args.fps if args.fps > 0 else None
    output_data = process_wham_results(results, video_fps, target_fps)

    # JSON 出力
    os.chdir(original_cwd)
    output_path = os.path.abspath(args.output) if not os.path.isabs(args.output) \
        else args.output
    with open(output_path, "w", encoding="utf-8") as f:
        json.dump(output_data, f, ensure_ascii=False)

    print(f"Done: {output_data['frameCount']} frames written to {output_path}",
          file=sys.stderr)
    print("PROGRESS:1.000", file=sys.stderr, flush=True)


if __name__ == "__main__":
    main()
