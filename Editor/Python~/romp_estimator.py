"""
ROMP (Simple-ROMP) を使用して動画から姿勢推定を行い、
Unity Humanoid 互換の JSON を出力する。

前提条件:
  - pip install simple-romp
  - SMPL モデルファイルの準備 (romp.prepare_smpl)

Usage:
    python romp_estimator.py --input video.mp4 --output pose.json [--fps 30] [--smooth 3.0]
"""

import argparse
import json
import math
import os
import sys
import contextlib
import io


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
    angle = math.sqrt(float(aa[0]) ** 2 + float(aa[1]) ** 2 + float(aa[2]) ** 2)
    if angle < 1e-8:
        return [0.0, 0.0, 0.0, 1.0]
    half = angle / 2.0
    s = math.sin(half) / angle
    return [float(aa[0]) * s, float(aa[1]) * s, float(aa[2]) * s, math.cos(half)]


def smpl_quat_to_unity(q):
    """SMPL 座標系 (右手系, Y-up) → Unity 座標系 (左手系, Y-up)。
    Z 軸を反転: qx, qy を反転、qz, qw はそのまま。"""
    return [-q[0], -q[1], q[2], q[3]]


def smpl_pos_to_unity(pos):
    """SMPL 位置 → Unity 位置。Z 軸を反転。"""
    return [float(pos[0]), float(pos[1]), float(-pos[2])]


def round6(v):
    return round(v, 6)


# ================================================================
# フレーム変換
# ================================================================

def convert_frame(smpl_thetas, cam_trans, time_sec):
    """SMPL パラメータ 1 フレーム分を Unity 互換 dict に変換。

    Args:
        smpl_thetas: (72,) axis-angle for 24 joints
        cam_trans: (3,) camera-space translation or None
        time_sec: float timestamp
    """
    bones = {}

    # Root (joint 0) rotation
    root_aa = smpl_thetas[:3]
    root_q = axis_angle_to_quaternion(root_aa)
    root_q_unity = smpl_quat_to_unity(root_q)

    hips_entry = {
        "rotation": list(map(round6, root_q_unity))
    }
    if cam_trans is not None:
        pos = smpl_pos_to_unity(cam_trans)
        hips_entry["position"] = list(map(round6, pos))
    bones["Hips"] = hips_entry

    # Body joints (1-23)
    for joint_idx in range(1, 24):
        if joint_idx not in SMPL_TO_UNITY:
            continue
        bone_name = SMPL_TO_UNITY[joint_idx]
        aa = smpl_thetas[joint_idx * 3: (joint_idx + 1) * 3]
        q = axis_angle_to_quaternion(aa)
        q_unity = smpl_quat_to_unity(q)
        bones[bone_name] = {
            "rotation": list(map(round6, q_unity))
        }

    return {
        "time": round(time_sec, 5),
        "bones": bones
    }


# ================================================================
# メイン
# ================================================================

def main():
    parser = argparse.ArgumentParser(
        description="ROMP で動画から姿勢推定 → Unity 互換 JSON")
    parser.add_argument("--input", "-i", required=True,
                        help="入力動画ファイルパス")
    parser.add_argument("--output", "-o", required=True,
                        help="出力 JSON ファイルパス")
    parser.add_argument("--fps", type=float, default=0,
                        help="出力 FPS (0=元の動画FPSを使用)")
    parser.add_argument("--smooth", type=float, default=3.0,
                        help="時系列スムージング係数 (小さいほど滑らか)")
    parser.add_argument("--onnx", action="store_true",
                        help="ONNX 推論を使用 (CPU 高速化)")
    args = parser.parse_args()

    # ROMP インポート
    try:
        import romp
    except ImportError:
        print("Error: simple-romp is not installed.\n"
              "Run: pip install simple-romp",
              file=sys.stderr)
        sys.exit(1)

    import cv2

    # 動画を開く
    cap = cv2.VideoCapture(args.input)
    if not cap.isOpened():
        print(f"Error: Cannot open video: {args.input}", file=sys.stderr)
        sys.exit(1)

    video_fps = cap.get(cv2.CAP_PROP_FPS)
    total_frames = int(cap.get(cv2.CAP_PROP_FRAME_COUNT))
    target_fps = args.fps if args.fps > 0 else video_fps
    frame_interval = max(1, round(video_fps / target_fps))
    actual_fps = video_fps / frame_interval

    print(f"Video: {args.input}", file=sys.stderr)
    print(f"  FPS: {video_fps:.1f}, Target: {actual_fps:.1f}, "
          f"Interval: {frame_interval}, Total: {total_frames}",
          file=sys.stderr)

    # ROMP 初期化
    print("Initializing ROMP model...", file=sys.stderr)
    settings = romp.main.default_settings
    settings.calc_smpl = True
    settings.temporal_optimize = True
    settings.smooth_coeff = args.smooth

    if args.onnx:
        settings.onnx = True
        print("  ONNX inference enabled", file=sys.stderr)

    # ROMP モデル初期化時の出力を抑制
    with contextlib.redirect_stdout(io.StringIO()):
        romp_model = romp.ROMP(settings)

    print("ROMP model ready.", file=sys.stderr)
    print("PROGRESS:0.000", file=sys.stderr, flush=True)

    frames = []
    frame_idx = 0
    processed = 0
    detected = 0

    while True:
        ret, frame = cap.read()
        if not ret:
            break

        if frame_idx % frame_interval == 0:
            processed += 1

            # ROMP 推論 (stdout を抑制して PROGRESS と混ざらないようにする)
            with contextlib.redirect_stdout(io.StringIO()):
                outputs = romp_model(frame)

            if outputs is not None and len(outputs) > 0:
                # smpl_thetas が利用可能か確認
                thetas = None
                cam_trans_val = None

                if isinstance(outputs, dict):
                    thetas = outputs.get('smpl_thetas')
                    cam_trans_val = outputs.get('cam_trans')
                elif hasattr(outputs, 'smpl_thetas'):
                    thetas = outputs.smpl_thetas
                    cam_trans_val = getattr(outputs, 'cam_trans', None)

                if thetas is not None and len(thetas) > 0:
                    detected += 1
                    # 最初の人物を使用
                    theta = thetas[0]  # (72,)
                    trans = cam_trans_val[0] if cam_trans_val is not None \
                        else None
                    time_sec = frame_idx / video_fps
                    frame_data = convert_frame(theta, trans, time_sec)
                    frames.append(frame_data)

            progress = frame_idx / max(total_frames, 1)
            print(f"PROGRESS:{progress:.3f}", file=sys.stderr, flush=True)

        frame_idx += 1

    cap.release()

    print(f"  Processed: {processed}, Detected: {detected}",
          file=sys.stderr)

    if len(frames) == 0:
        print("Error: No poses detected in video.", file=sys.stderr)
        sys.exit(1)

    output_data = {
        "fps": round(actual_fps, 2),
        "frameCount": len(frames),
        "frames": frames
    }

    with open(args.output, "w", encoding="utf-8") as f:
        json.dump(output_data, f, ensure_ascii=False)

    print(f"Done: {len(frames)} frames → {args.output}", file=sys.stderr)
    print("PROGRESS:1.000", file=sys.stderr, flush=True)


if __name__ == "__main__":
    main()
