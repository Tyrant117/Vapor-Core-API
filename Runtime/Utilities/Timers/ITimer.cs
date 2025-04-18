using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Vapor
{
    public interface ITimer
    {
        double StartTime { get; }
        double Elapsed { get; }
    }
}
