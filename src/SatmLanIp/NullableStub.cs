// Compiles against Il2Cppmscorlib without pulling real NRT attributes from it.
namespace System.Runtime.CompilerServices
{
    internal sealed class NullableAttribute : System.Attribute
    {
        public NullableAttribute(byte _) { }
        public NullableAttribute(byte[] _) { }
    }

    internal sealed class NullableContextAttribute : System.Attribute
    {
        public NullableContextAttribute(byte _) { }
    }
}
