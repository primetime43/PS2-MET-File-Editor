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
                Assert.True(viewer.ClientSize.Width >= 1180);
                viewer.Close();
            }
            catch (Exception exception) { failure = exception; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(10)), "Viewer test did not finish.");
        Assert.Null(failure);
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
                structure.Write((ushort)0); structure.Write((ushort)0);
                structure.Write((ushort)1); structure.Write((ushort)2);
            }));
            writer.Write(Chunk(0x03, _ => { }));
        });
        return Chunk(0x0B, writer =>
        {
            writer.Write(worldStruct); writer.Write(sector); writer.Write(Chunk(0x03, _ => { }));
        });
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

    public void Dispose()
    {
        if (Directory.Exists(_temp)) Directory.Delete(_temp, recursive: true);
    }
}
