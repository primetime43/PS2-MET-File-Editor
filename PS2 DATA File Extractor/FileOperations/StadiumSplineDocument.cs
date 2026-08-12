using System.Buffers.Binary;
using System.Numerics;

namespace PS2_DATA_File_Extractor.FileOperations;

public sealed class StadiumSplineDocument
{
    private const int CountOffset = 0x2c, TypeOffset = 0x30, PointsOffset = 0x34, PointSize = 12;
    private readonly byte[] _original;
    private readonly List<Vector3> _points;
    private readonly int _originalPointCount;

    private StadiumSplineDocument(string sourcePath, byte[] original, List<Vector3> points)
    {
        SourcePath = NormalizePath(sourcePath);
        _original = original;
        _points = points;
        _originalPointCount = points.Count;
        SplineType = BinaryPrimitives.ReadInt32LittleEndian(original.AsSpan(TypeOffset, 4));
    }

    public string SourcePath { get; }
    public int SplineType { get; }
    public IReadOnlyList<Vector3> Points => _points;
    public bool IsChanged => !_original.AsSpan().SequenceEqual(Serialize());

    public static StadiumSplineDocument Parse(string sourcePath, byte[] data)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        ArgumentNullException.ThrowIfNull(data);
        IReadOnlyList<Vector3> points = StadiumAmbientPreviewBuilder.ParseSpline(data);
        if (points.Count < 2)
            throw new InvalidDataException($"'{sourcePath}' is not a readable movement spline with at least two points.");
        return new StadiumSplineDocument(sourcePath, data.ToArray(), points.ToList());
    }

    public void SetPoint(int index, Vector3 value)
    {
        ValidateFinite(value);
        _points[index] = value;
    }

    public int InsertAfter(int index, Vector3 value)
    {
        ValidateFinite(value);
        int target = Math.Clamp(index + 1, 0, _points.Count);
        _points.Insert(target, value);
        return target;
    }

    public int Duplicate(int index) => InsertAfter(index, _points[index]);

    public int RemoveAt(int index)
    {
        if (_points.Count <= 2) throw new InvalidOperationException("A movement spline must keep at least two points.");
        _points.RemoveAt(index);
        return Math.Clamp(index, 0, _points.Count - 1);
    }

    public int Move(int index, int offset)
    {
        int target = Math.Clamp(index + offset, 0, _points.Count - 1);
        if (target == index) return index;
        (_points[index], _points[target]) = (_points[target], _points[index]);
        return target;
    }

    public void Reset()
    {
        _points.Clear();
        _points.AddRange(StadiumAmbientPreviewBuilder.ParseSpline(_original));
    }

    public byte[] Serialize()
    {
        int originalPointsEnd = checked(PointsOffset + _originalPointCount * PointSize);
        int suffixLength = Math.Max(0, _original.Length - originalPointsEnd);
        byte[] result = new byte[checked(PointsOffset + _points.Count * PointSize + suffixLength)];
        _original.AsSpan(0, Math.Min(PointsOffset, _original.Length)).CopyTo(result);
        BinaryPrimitives.WriteInt32LittleEndian(result.AsSpan(4, 4), result.Length - 12);
        BinaryPrimitives.WriteInt32LittleEndian(result.AsSpan(CountOffset, 4), _points.Count);
        for (int index = 0; index < _points.Count; index++)
        {
            int offset = PointsOffset + index * PointSize;
            WriteFloat(result.AsSpan(offset, 4), _points[index].X);
            WriteFloat(result.AsSpan(offset + 4, 4), _points[index].Y);
            WriteFloat(result.AsSpan(offset + 8, 4), _points[index].Z);
        }
        if (suffixLength > 0)
            _original.AsSpan(originalPointsEnd, suffixLength).CopyTo(result.AsSpan(result.Length - suffixLength));
        return result;
    }

    public static string NormalizePath(string value)
    {
        string path = value.Split(';', 2)[0].Trim().TrimStart('/').Replace('\\', '/');
        return path.StartsWith("data/", StringComparison.OrdinalIgnoreCase) ? path : "data/" + path;
    }

    private static void WriteFloat(Span<byte> destination, float value) =>
        BinaryPrimitives.WriteInt32LittleEndian(destination, BitConverter.SingleToInt32Bits(value));

    private static void ValidateFinite(Vector3 value)
    {
        if (!float.IsFinite(value.X) || !float.IsFinite(value.Y) || !float.IsFinite(value.Z))
            throw new ArgumentOutOfRangeException(nameof(value), "Waypoint coordinates must be finite numbers.");
    }
}
