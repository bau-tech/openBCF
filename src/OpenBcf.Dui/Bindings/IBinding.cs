using OpenBcf.Dui.Bridge;

namespace OpenBcf.Dui.Bindings;

/// <summary>
/// A host-specific (Revit, eventually Tekla) object exposed to the frontend under <see cref="Name"/>.
/// Every public, non-special-name method becomes callable from JS via <see cref="Parent"/>.
/// </summary>
public interface IBinding
{
    string Name { get; }

    IBrowserBridge Parent { get; }
}
