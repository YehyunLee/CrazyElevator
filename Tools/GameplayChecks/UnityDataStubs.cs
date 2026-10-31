// Only Unity's data/Inspector types are substituted. Both game-rule source files
// compile unchanged; this harness does not exercise rendering or Input System.
namespace UnityEngine
{
    public class ScriptableObject { }
    public sealed class CreateAssetMenuAttribute : System.Attribute
    {
        public string menuName;
        public string fileName;
    }
    public sealed class HeaderAttribute : System.Attribute { public HeaderAttribute(string text) { } }
    public sealed class TooltipAttribute : System.Attribute { public TooltipAttribute(string text) { } }
    public sealed class TextAreaAttribute : System.Attribute { }
    public sealed class RangeAttribute : System.Attribute { public RangeAttribute(float min, float max) { } }
    public sealed class MinAttribute : System.Attribute { public MinAttribute(float value) { } }
    public static class Mathf
    {
        public static int Max(int a, int b) => System.Math.Max(a, b);
        public static int Clamp(int value, int min, int max) => System.Math.Clamp(value, min, max);
    }
}
namespace CrazyElevator.Shared { public sealed class PassengerView { } }
