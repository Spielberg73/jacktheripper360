using System.Collections.Generic;
using JackTheRipper360.Core.Models;

namespace JackTheRipper360.Core.Animation
{
    /// <summary>
    /// Platform-agnostic animation clip data extracted from Xbox 360 formats.
    /// </summary>
    public class AnimationClipData
    {
        public string Name { get; set; }
        public float Duration { get; set; }
        public float FrameRate { get; set; } = 30f;
        public List<BoneChannel> Channels { get; set; } = new List<BoneChannel>();
    }

    public class BoneChannel
    {
        public string BoneName { get; set; }
        public int BoneIndex { get; set; }
        public List<Keyframe<Vector3f>> PositionKeys { get; set; } = new List<Keyframe<Vector3f>>();
        public List<Keyframe<Vector4f>> RotationKeys { get; set; } = new List<Keyframe<Vector4f>>();
        public List<Keyframe<Vector3f>> ScaleKeys { get; set; } = new List<Keyframe<Vector3f>>();
    }

    public class Keyframe<T>
    {
        public float Time { get; set; }
        public T Value { get; set; }

        public Keyframe() { }
        public Keyframe(float time, T value)
        {
            Time = time;
            Value = value;
        }
    }
}
