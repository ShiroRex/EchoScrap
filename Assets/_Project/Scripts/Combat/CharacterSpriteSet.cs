using System;
using UnityEngine;

namespace EchoScrap.Combat
{
    public enum CharacterMotion { Idle, Walk, Dash, Death }
    public enum CharacterFacing { Down, Right, Up, Left }

    [CreateAssetMenu(menuName = "EchoScrap/Art/Character Sprite Set")]
    public sealed class CharacterSpriteSet : ScriptableObject
    {
        [Serializable]
        public struct Frame
        {
            public Vector4 uvRect;
            public float duration;
        }

        [Serializable]
        public sealed class Clip
        {
            public string name;
            public bool loop;
            public Frame[] frames;
            public float Duration
            {
                get
                {
                    float total = 0f;
                    for (int i = 0; i < frames.Length; i++) total += frames[i].duration;
                    return total;
                }
            }

            public int FrameAt(float time)
            {
                if (loop) time = Mathf.Repeat(time, Duration);
                for (int i = 0; i < frames.Length - 1; i++)
                {
                    if (time < frames[i].duration) return i;
                    time -= frames[i].duration;
                }
                return frames.Length - 1;
            }
        }

        public Clip[] clips;
        [Min(1f)] public float pixelsPerUnit = 24f;
        [Min(0f)] public float movingThreshold = 0.15f;
        [Range(0f, 0.5f)] public float directionHysteresis = 0.12f;
        public Clip GetClip(CharacterMotion motion, CharacterFacing facing) => clips[(int)facing * 4 + (int)motion];
    }
}
