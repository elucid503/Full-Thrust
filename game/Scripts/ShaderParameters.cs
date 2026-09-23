using System.Collections.Concurrent;

using Godot;

namespace FullThrust.Game;

// A plain string where Godot takes a StringName builds, and later finalizes, a native name on
// every call; most of these run per material per frame.
internal static class ShaderParameters {

    private static readonly ConcurrentDictionary<string, StringName> Names = new();

    private static StringName Name(string name) => Names.GetOrAdd(name, static key => new StringName(key));

    public static void SetParameter(this ShaderMaterial material, string name, Variant value) => material.SetShaderParameter(Name(name), value);

    public static Variant GetParameter(this ShaderMaterial material, string name) => material.GetShaderParameter(Name(name));

    public static void SetInstanceParameter(this GeometryInstance3D instance, string name, Variant value) => instance.SetInstanceShaderParameter(Name(name), value);

}
