using UnityEngine;
using System.Collections;
using System.Collections.Generic;

namespace Timeline
{
    public class Keyframe
    {
        public object value;
        public Interpolable parent;
        public AnimationCurve curve;

        public Timeline.KeyframeDisplay keyframeDisplay;

        public Keyframe(object value, Interpolable parent, AnimationCurve curve)
        {
            this.value = value;
            this.parent = parent;
            this.curve = curve;
        }

        public Keyframe(Keyframe other)
        {
            value = other.value;
            parent = other.parent;
            curve = new AnimationCurve(other.curve.keys);
        }
    }   
}
