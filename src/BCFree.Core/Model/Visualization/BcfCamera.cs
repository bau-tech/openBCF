namespace BCFree.Core.Model.Visualization;

public sealed record BcfCamera(
    BcfCameraType Type,
    Point3D ViewPoint,
    Point3D Direction,
    Point3D UpVector,
    double? FieldOfView = null,
    double? ViewToWorldScale = null);
