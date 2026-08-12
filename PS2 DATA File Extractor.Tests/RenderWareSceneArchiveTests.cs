using System.Drawing;
using System.Numerics;
using System.Windows.Forms;
using PS2_DATA_File_Extractor;
using PS2_DATA_File_Extractor.FileOperations;

namespace PS2_DATA_File_Extractor.Tests;

public sealed class RenderWareSceneArchiveTests : IDisposable
{
    private readonly string _temp = Path.Combine(Path.GetTempPath(), $"rw-scene-tests-{Guid.NewGuid():N}");

    [Fact]
    public void ParsesRigidDffFrameAtomicAndGeometry()
    {
        RenderWareScene scene = RenderWareSceneParser.Parse("data/models/test.dff",
            RenderWareAssetKind.DffModel, CreateRigidDff());

        RenderWareSceneMesh mesh = Assert.Single(scene.Meshes);
        Assert.Equal(3, scene.VertexCount);
        Assert.Equal(1, scene.TriangleCount);
        Assert.Equal(new Vector3(10, 20, 30), mesh.Vertices[0].Position);
        Assert.Equal(new RenderWareTriangle(0, 1, 2, 0), Assert.Single(mesh.Triangles));
    }

    [Fact]
    public void ParsesRwsWorldAtomicSectorGeometry()
    {
        RenderWareScene scene = RenderWareSceneParser.Parse("data/fields/test/test.rws",
            RenderWareAssetKind.RwsScene, CreateWorldRws());

        Assert.Equal(1, scene.WorldSectorCount);
        Assert.Equal(0, scene.PlaneSectorCount);
        Assert.Equal(3, scene.VertexCount);
        Assert.Equal(1, scene.TriangleCount);
        Assert.Equal(Vector3.UnitX, scene.Meshes[0].Vertices[1].Position);
    }

    [Fact]
    public void DecodesPlatformIndependentTextureDictionary()
    {
        RenderWareScene scene = RenderWareSceneParser.Parse("data/fields/test/test.rws",
            RenderWareAssetKind.RwsScene, CreatePiTextureDictionary());

        RenderWareTexture texture = Assert.Single(scene.Textures).Value;
        Assert.Equal("grass00", Assert.Single(scene.NativeTextureNames));
        Assert.Equal(2, texture.Width);
        Assert.Equal(2, texture.Height);
        Assert.Equal(Color.FromArgb(255, 10, 20, 30).ToArgb(), texture.Pixels[0]);
        Assert.Equal(Color.FromArgb(128, 90, 80, 70).ToArgb(), texture.Pixels[1]);
    }

    [Fact]
    public void ExportsWavefrontObjAndMaterialFile()
    {
        Directory.CreateDirectory(_temp);
        RenderWareScene scene = RenderWareSceneParser.Parse("test.dff",
            RenderWareAssetKind.DffModel, CreateRigidDff());
        string obj = Path.Combine(_temp, "test.obj");

        RenderWareSceneArchive.ExportObj(scene, obj);

        Assert.Contains("v 10 20 30", File.ReadAllText(obj));
        Assert.Contains("f 1/1/1 2/2/2 3/3/3", File.ReadAllText(obj));
        Assert.True(File.Exists(Path.Combine(_temp, "test.mtl")));
    }

    [Fact]
    public void RetailFieldCoordinatesExposeGameplayCamerasBasesAndFielderSpawns()
    {
        BackyardCameraPreset batting = BackyardFieldCoordinates.CameraPresets[0];

        Assert.Equal("Game batting camera", batting.Name);
        Assert.Equal(new Vector3(0F, 75.2F, 509.1F), batting.Position);
        Assert.Equal(180F, batting.HeadingDegrees);
        Assert.Equal(new Vector3(814.5F, 0F, -848F), BackyardFieldCoordinates.Bases["First base"]);
        Assert.Equal(new Vector3(0F, 0F, -1696F), BackyardFieldCoordinates.Bases["Second base"]);
        Assert.Equal(24, BackyardFieldCoordinates.InfieldSpawns.Count);
        Assert.Equal(27, BackyardFieldCoordinates.OutfieldSpawns.Count);
    }

    [Fact]
    public void PreviewCanEnterAndLeaveRecoveredFieldCameraMode()
    {
        using RenderWareScenePreviewControl preview = new() { ClientSize = new Size(640, 360) };
        preview.Scene = RenderWareSceneParser.Parse("data/models/test.dff",
            RenderWareAssetKind.DffModel, CreateRigidDff());

        preview.SetFieldCamera(BackyardFieldCoordinates.CameraPresets[0]);
        using Bitmap bitmap = new(640, 360);
        preview.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));

        Assert.True(preview.IsFieldCamera);
        Assert.Equal(new Vector3(0F, 75.2F, 509.1F), preview.FieldCameraPosition);
        preview.ResetView();
        Assert.False(preview.IsFieldCamera);
    }

    [Theory]
    [InlineData("aquadome", "data/fields/aquadome_rws/aquadome_ps2.rws")]
    [InlineData("driveinnight", "data/fields/driveinnight_rws/drivein_night.rws")]
    [InlineData("memorial", "data/fields/memorial_rws/hem_field.rws")]
    [InlineData("wheelernight", "data/fields/wheelernight_rws/wheeler_ps2_night.rws")]
    public void StadiumFielddataFoldersMapToRetailRwsScenes(string folder, string expected)
    {
        Assert.Equal(expected, RenderWareSceneArchive.GetStadiumScenePath(folder));
    }

    [Fact]
    public void PreviewAppliesLiveFielddataAmbientLight()
    {
        using RenderWareScenePreviewControl preview = new();

        preview.EnvironmentLight = new Vector4(0.8F, 0.7F, 0.6F, 1F);

        Assert.Equal(new Vector4(0.8F, 0.7F, 0.6F, 1F), preview.EnvironmentLight);
    }

    [Fact]
    public void ParsesRetailSplinePointTable()
    {
        byte[] spline = new byte[0x34 + 24];
        BitConverter.GetBytes(0x0cU).CopyTo(spline, 0);
        BitConverter.GetBytes(2).CopyTo(spline, 0x2c);
        WriteVector(spline, 0x34, new Vector3(12.5F, -40F, 3.25F));
        WriteVector(spline, 0x40, new Vector3(22F, 18.75F, -9F));

        IReadOnlyList<Vector3> points = StadiumAmbientPreviewBuilder.ParseSpline(spline);

        Assert.Equal(2, points.Count);
        Assert.Equal(new Vector3(12.5F, -40F, 3.25F), points[0]);
        Assert.Equal(new Vector3(22F, 18.75F, -9F), points[1]);
    }

    [Fact]
    public void RejectsTruncatedSplineInsteadOfReadingPastEntry()
    {
        byte[] spline = new byte[0x34 + 12];
        BitConverter.GetBytes(0x0cU).CopyTo(spline, 0);
        BitConverter.GetBytes(2).CopyTo(spline, 0x2c);

        Assert.Empty(StadiumAmbientPreviewBuilder.ParseSpline(spline));
    }

    [Fact]
    public void SamplesSplineByDistanceInsteadOfRawPointIndex()
    {
        Vector3[] points = [Vector3.Zero, new Vector3(10, 0, 0), new Vector3(10, 0, 30)];

        StadiumAmbientPathSample quarter = StadiumAmbientPreviewBuilder.SamplePath(points, 0.25F);
        StadiumAmbientPathSample half = StadiumAmbientPreviewBuilder.SamplePath(points, 0.5F);

        Assert.Equal(new Vector3(10, 0, 0), quarter.Position);
        Assert.Equal(new Vector3(10, 0, 10), half.Position);
        Assert.Equal(Vector3.UnitZ, half.Direction);
    }

    [Fact]
    public void SplineSamplingClampsEndsAndHandlesRepeatedPoints()
    {
        Vector3[] points = [new Vector3(4, 5, 6), new Vector3(4, 5, 6), new Vector3(7, 5, 6)];

        Assert.Equal(points[0], StadiumAmbientPreviewBuilder.SamplePath(points, -2F).Position);
        Assert.Equal(points[^1], StadiumAmbientPreviewBuilder.SamplePath(points, 2F).Position);
    }

    [Theory]
    [InlineData("speed 3.0;", 3F, 4D)]
    [InlineData("randFloatSpeed 2.0 4.0;", 3F, 4D)]
    [InlineData("anim bird.anm; 0.0 0.0;", 1F, 12D)]
    public void DerivesStablePreviewCycleFromRetailSpeedDirectives(
        string directive, float expectedSpeed, double expectedDuration)
    {
        FieldDataAmbient ambient = Assert.Single(FieldDataDocument.Parse(
            $"field {{\r\n  numAmbs 1;\r\n}}\r\namb {{\r\n  {directive}\r\n}}\r\n").Ambients);

        Assert.Equal(expectedSpeed, StadiumAmbientPreviewBuilder.GetPreviewSpeed(ambient));
        Assert.Equal(expectedDuration, StadiumAmbientPreviewBuilder.EstimatePreviewDuration(ambient));
    }

    [Fact]
    public void PlaybackDeltaMovesPlacedModelToSampleAndFacesItsTangent()
    {
        FieldDataAmbient ambient = Assert.Single(FieldDataDocument.Parse(
            "field {\r\n  numAmbs 1;\r\n}\r\namb {\r\n  pos 10 0 20;\r\n  hpr 0 0 0;\r\n}\r\n").Ambients);
        StadiumAmbientPathSample sample = new(new Vector3(30, 0, 40), Vector3.UnitX);

        Matrix4x4 delta = StadiumAmbientPreviewBuilder.CreatePlaybackDelta(
            ambient, new Vector3(10, 0, 20), sample, facePath: true);

        AssertVectorNear(new Vector3(30, 0, 40), Vector3.Transform(new Vector3(10, 0, 20), delta));
        AssertVectorNear(Vector3.UnitX, Vector3.Normalize(Vector3.TransformNormal(Vector3.UnitZ, delta)));
    }

    [Theory]
    [InlineData(3D, 12D, 2F, true, true, 0.5F)]
    [InlineData(3D, 12D, 2F, false, true, 1F)]
    [InlineData(3D, 12D, 2F, false, false, 2F)]
    public void AmbientAnimationCanSyncLoopOrClampIndependentlyOfItsPath(
        double position, double pathDuration, float animationDuration,
        bool sync, bool loop, float expected)
    {
        float actual = StadiumAmbientPreviewBuilder.GetAnimationPlaybackTime(
            position, pathDuration, animationDuration, sync, loop);

        Assert.Equal(expected, actual, 4);
    }

    [Fact]
    public void SplineDocumentRoundTripsUnknownHeaderAndSuffixBytes()
    {
        byte[] spline = CreateSpline([Vector3.Zero, Vector3.UnitX, Vector3.UnitY], type: 7, suffix: [0xAA, 0xBB]);

        StadiumSplineDocument document = StadiumSplineDocument.Parse("Fields/Test/path.spl", spline);

        Assert.False(document.IsChanged);
        Assert.Equal(7, document.SplineType);
        Assert.Equal("data/Fields/Test/path.spl", document.SourcePath);
        Assert.Equal(spline, document.Serialize());
    }

    [Fact]
    public void SplineDocumentAddsMovesEditsDeletesAndResetsPointsSafely()
    {
        byte[] spline = CreateSpline([Vector3.Zero, new Vector3(10, 0, 0), new Vector3(20, 0, 0)], type: 2);
        StadiumSplineDocument document = StadiumSplineDocument.Parse("data/test.spl", spline);

        int inserted = document.InsertAfter(0, new Vector3(5, 0, 0));
        document.SetPoint(inserted, new Vector3(6, 1, 2));
        inserted = document.Move(inserted, 1);
        int remaining = document.RemoveAt(inserted);
        document.SetPoint(remaining, new Vector3(11, 2, 3));
        byte[] changed = document.Serialize();

        Assert.Equal(3, document.Points.Count);
        Assert.Equal(3, BitConverter.ToInt32(changed, 0x2c));
        Assert.Equal(changed.Length - 12, BitConverter.ToInt32(changed, 4));
        Assert.True(document.IsChanged);
        document.Reset();
        Assert.False(document.IsChanged);
        Assert.Equal(3, document.Points.Count);
        Assert.InRange(remaining, 0, 2);
    }

    [Fact]
    public void SplineDocumentRefusesToDeleteBelowTwoPoints()
    {
        StadiumSplineDocument document = StadiumSplineDocument.Parse("data/test.spl",
            CreateSpline([Vector3.Zero, Vector3.UnitX], type: 1));

        Assert.Throws<InvalidOperationException>(() => document.RemoveAt(0));
    }

    [Fact]
    public void ResolvesRetailAmbientModelWithArgumentsAfterItsFilename()
    {
        Directory.CreateDirectory(_temp);
        string metPath = Path.Combine(_temp, "DATA.MET");
        CreateArchive(metPath, "data/fields/commonambients/crowMesh.dff", CreateRigidDff());
        RenderWareSceneArchive archive = RenderWareSceneArchive.Load(metPath);

        RenderWareAssetFile? model = archive.FindAmbientModel(
            "Fields/CommonAmbients;", "crowMesh.dff; 0.0 0.0;", "drivein");

        Assert.NotNull(model);
        Assert.Equal("data/fields/commonambients/crowMesh.dff", model.Path);
    }

    [Fact]
    public void ViewerOpensAfterApplyingItsDefaultSplitterLayout()
    {
        Directory.CreateDirectory(_temp);
        string metPath = Path.Combine(_temp, "DATA.MET");
        CreateArchive(metPath, "data/models/test.dff", CreateRigidDff());
        RenderWareSceneArchive archive = RenderWareSceneArchive.Load(metPath);
        Exception? failure = null;
        Thread thread = new(() =>
        {
            try
            {
                using RenderWareModelViewerForm viewer = new(archive, metPath);
                viewer.Show();
                Application.DoEvents();
                // Showing the form exercises the splitter initialization. Do not assert
                // desktop pixels: WinForms clamps shown forms to the CI virtual screen.
                Assert.True(ContainsControl<RenderWareScenePreviewControl>(viewer));
                viewer.Close();
            }
            catch (Exception exception) { failure = exception; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(10)), "Viewer test did not finish.");
        Assert.Null(failure);
    }

    [Fact]
    public void DetachedPreviewOpensAsIndependentResizableWindow()
    {
        RenderWareScene scene = RenderWareSceneParser.Parse("data/models/test.dff",
            RenderWareAssetKind.DffModel, CreateRigidDff());
        Exception? failure = null;
        Thread thread = new(() =>
        {
            try
            {
                using RenderWareDetachedPreviewForm preview = new(scene, true, false, false, false, false,
                    Vector4.One, BackyardFieldCoordinates.CameraPresets[0]);
                preview.Show();
                Application.DoEvents();
                Assert.True(preview.MaximizeBox);
                Assert.True(ContainsControl<RenderWareScenePreviewControl>(preview));
                preview.Close();
            }
            catch (Exception exception) { failure = exception; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(10)), "Detached preview test did not finish.");
        Assert.Null(failure);
    }

    private static bool ContainsControl<T>(Control parent) where T : Control
    {
        foreach (Control child in parent.Controls)
        {
            if (child is T || ContainsControl<T>(child)) return true;
        }
        return false;
    }

    private static byte[] CreateRigidDff()
    {
        byte[] clumpStruct = Chunk(0x01, writer =>
        {
            writer.Write(1); writer.Write(0); writer.Write(0);
        });
        byte[] frameList = Chunk(0x0E, writer =>
        {
            writer.Write(Chunk(0x01, frame =>
            {
                frame.Write(1);
                WriteMatrix(frame, new Vector3(10, 20, 30));
                frame.Write(-1); frame.Write(0);
            }));
            writer.Write(Chunk(0x03, _ => { }));
        });
        byte[] geometry = Chunk(0x0F, writer =>
        {
            writer.Write(Chunk(0x01, structure =>
            {
                structure.Write(0x02); structure.Write(1); structure.Write(3); structure.Write(1);
                structure.Write((ushort)1); structure.Write((ushort)0);
                structure.Write((ushort)0); structure.Write((ushort)2);
                structure.Write(0F); structure.Write(0F); structure.Write(0F); structure.Write(2F);
                structure.Write(1); structure.Write(0);
                WriteVector(structure, Vector3.Zero);
                WriteVector(structure, Vector3.UnitX);
                WriteVector(structure, Vector3.UnitY);
            }));
            writer.Write(Chunk(0x03, _ => { }));
        });
        byte[] geometryList = Chunk(0x1A, writer =>
        {
            writer.Write(Chunk(0x01, structure => structure.Write(1)));
            writer.Write(geometry);
        });
        byte[] atomic = Chunk(0x14, writer =>
        {
            writer.Write(Chunk(0x01, structure =>
            {
                structure.Write(0); structure.Write(0); structure.Write(4); structure.Write(0);
            }));
            writer.Write(Chunk(0x03, _ => { }));
        });
        return Chunk(0x10, writer =>
        {
            writer.Write(clumpStruct); writer.Write(frameList); writer.Write(geometryList);
            writer.Write(atomic); writer.Write(Chunk(0x03, _ => { }));
        });
    }

    private static byte[] CreateWorldRws()
    {
        byte[] worldStruct = Chunk(0x01, writer =>
        {
            writer.Write(1); WriteVector(writer, Vector3.Zero);
            writer.Write(1); writer.Write(3); writer.Write(0); writer.Write(1); writer.Write(0);
            writer.Write(0x02);
            WriteVector(writer, Vector3.One); WriteVector(writer, Vector3.Zero);
        });
        byte[] sector = Chunk(0x09, writer =>
        {
            writer.Write(Chunk(0x01, structure =>
            {
                structure.Write(0); structure.Write(1); structure.Write(3);
                WriteVector(structure, Vector3.Zero); WriteVector(structure, Vector3.One);
                structure.Write(0x02); structure.Write(0);
                WriteVector(structure, Vector3.Zero);
                WriteVector(structure, Vector3.UnitX);
                WriteVector(structure, Vector3.UnitY);
                structure.Write((ushort)0); structure.Write((ushort)1);
                structure.Write((ushort)2); structure.Write((ushort)0);
            }));
            writer.Write(Chunk(0x03, _ => { }));
        });
        return Chunk(0x0B, writer =>
        {
            writer.Write(worldStruct); writer.Write(sector); writer.Write(Chunk(0x03, _ => { }));
        });
    }

    private static byte[] CreatePiTextureDictionary()
    {
        byte[] image = Chunk(0x18, writer =>
        {
            writer.Write(Chunk(0x01, structure =>
            {
                structure.Write(2); structure.Write(2); structure.Write(8); structure.Write(4);
            }));
            writer.Write(new byte[] { 1, 2, 0, 0, 2, 1, 0, 0 });
            byte[] palette = new byte[256 * 4];
            palette[4] = 10; palette[5] = 20; palette[6] = 30; palette[7] = 255;
            palette[8] = 90; palette[9] = 80; palette[10] = 70; palette[11] = 128;
            writer.Write(palette);
        });
        byte[] texture = Chunk(0x06, writer =>
        {
            writer.Write(Chunk(0x01, structure => structure.Write(0x00001106)));
            writer.Write(Chunk(0x02, name => name.Write(System.Text.Encoding.ASCII.GetBytes("grass00\0"))));
            writer.Write(Chunk(0x02, name => name.Write((byte)0)));
            writer.Write(Chunk(0x03, _ => { }));
        });
        return Chunk(0x23, writer =>
        {
            writer.Write(0x00010001);
            writer.Write(1);
            writer.Write(image);
            writer.Write(texture);
        });
    }

    private static byte[] CreateSpline(IReadOnlyList<Vector3> points, int type, byte[]? suffix = null)
    {
        suffix ??= [];
        byte[] data = new byte[0x34 + points.Count * 12 + suffix.Length];
        BitConverter.GetBytes(0x0cU).CopyTo(data, 0);
        BitConverter.GetBytes(data.Length - 12).CopyTo(data, 4);
        BitConverter.GetBytes(0x1803ffffU).CopyTo(data, 8);
        for (int index = 12; index < 0x2c; index++) data[index] = (byte)(index * 3);
        BitConverter.GetBytes(points.Count).CopyTo(data, 0x2c);
        BitConverter.GetBytes(type).CopyTo(data, 0x30);
        for (int index = 0; index < points.Count; index++) WriteVector(data, 0x34 + index * 12, points[index]);
        suffix.CopyTo(data, data.Length - suffix.Length);
        return data;
    }

    private static byte[] Chunk(uint id, Action<BinaryWriter> write)
    {
        using MemoryStream payload = new();
        using (BinaryWriter writer = new(payload, System.Text.Encoding.UTF8, leaveOpen: true)) write(writer);
        using MemoryStream result = new();
        using BinaryWriter output = new(result);
        output.Write(id); output.Write(checked((int)payload.Length)); output.Write(0x1803FFFFU);
        output.Write(payload.ToArray());
        return result.ToArray();
    }

    private static void CreateArchive(string path, string entryPath, byte[] payload)
    {
        byte[] name = System.Text.Encoding.UTF8.GetBytes(entryPath);
        int dataOffset = 8 + 12 + name.Length;
        using FileStream stream = File.Create(path);
        using BinaryWriter writer = new(stream);
        writer.Write(dataOffset);
        writer.Write(payload.Length);
        writer.Write(dataOffset);
        writer.Write(payload.Length);
        writer.Write(name.Length);
        writer.Write(name);
        writer.Write(payload);
    }

    private static void WriteMatrix(BinaryWriter writer, Vector3 translation)
    {
        writer.Write(1F); writer.Write(0F); writer.Write(0F);
        writer.Write(0F); writer.Write(1F); writer.Write(0F);
        writer.Write(0F); writer.Write(0F); writer.Write(1F);
        WriteVector(writer, translation);
    }

    private static void WriteVector(BinaryWriter writer, Vector3 value)
    {
        writer.Write(value.X); writer.Write(value.Y); writer.Write(value.Z);
    }

    private static void WriteVector(byte[] destination, int offset, Vector3 value)
    {
        BitConverter.GetBytes(value.X).CopyTo(destination, offset);
        BitConverter.GetBytes(value.Y).CopyTo(destination, offset + 4);
        BitConverter.GetBytes(value.Z).CopyTo(destination, offset + 8);
    }

    private static void AssertVectorNear(Vector3 expected, Vector3 actual)
    {
        Assert.InRange(Vector3.Distance(expected, actual), 0F, 0.0001F);
    }

    public void Dispose()
    {
        if (Directory.Exists(_temp)) Directory.Delete(_temp, recursive: true);
    }
}
