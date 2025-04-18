using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Vapor.Inspector
{
    public struct EnumData
    {
        public Enum[] values;

        public int[] flagValues;

        public string[] displayNames;

        public string[] names;

        public string[] tooltip;

        public bool flags;

        public Type underlyingType;

        public bool unsigned;

        public bool serializable;
    }
}
