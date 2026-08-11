using System.Text;
using System.Numerics;
using System.Drawing;
using PS2_DATA_File_Extractor;
using PS2_DATA_File_Extractor.FileOperations;

namespace PS2_DATA_File_Extractor.Tests;

public sealed class RenderWareAnimationArchiveTests : IDisposable
{
    private readonly string _tempDirectory = Path.Combine(
        Path.GetTempPath(), $"animation-tests-{Guid.NewGuid():N}");

    [Fact]
    public void ParsesStandardTracksAndSamplesTranslation()
    {
        RenderWareAnimationFile file = RenderWareAnimationFile.Parse(
            "data/batting/test/test_swing.anm", CreateStandardAnimation());

        Assert.Equal(RenderWareAnimationFile.StandardScheme, file.SchemeId);
        Assert.Equal(5, file.FrameCount);
        Assert.Equal(2, file.TrackCount);
        Assert.Equal(new[] { 0, 2, 4 }, file.Tracks[0].FrameIndices);
        Assert.Equal(new[] { 1, 3 }, file.Tracks[1].FrameIndices);
        Assert.Null(file.Frames[0].PreviousFrameIndex);
        Assert.Equal(2, file.Frames[4].PreviousFrameIndex);

        RenderWareAnimationTransform sampled = file.SampleTrack(0, 1.5F);
        Assert.Equal(15F, sampled.TranslationX, 3);
        Assert.Equal(30F, sampled.TranslationY, 3);
    }

    [Fact]
    public void ParsesCompressedFramesAndAppliesTranslationScaleAndOffset()
    {
        RenderWareAnimationFile file = RenderWareAnimationFile.Parse(
            "data/characters/test/compressed.anm", CreateCompressedAnimation());

        Assert.Equal(RenderWareAnimationFile.CompressedScheme, file.SchemeId);
        Assert.Equal(2, file.TrackCount);
        Assert.Equal(4, file.FrameCount);
        RenderWareAnimationTransform transform = file.Frames[2].Transform;
        Assert.Equal(1F, transform.QuaternionW, 3);
        Assert.Equal(12F, transform.TranslationX, 3);
        Assert.Equal(23F, transform.TranslationY, 3);
        Assert.Equal(34F, transform.TranslationZ, 3);
        Assert.Equal(0, file.Frames[2].PreviousFrameIndex);
    }

    [Fact]
    public void ScalesDurationAndSerializesOnlyEditableTimingFields()
    {
        byte[] original = CreateStandardAnimation();
        RenderWareAnimationFile file = RenderWareAnimationFile.Parse("test.anm", original);

        file.ScaleToDuration(4F);
        file.SetKeyFrameTime(2, 1.5F);
        byte[] edited = file.Serialize();
        RenderWareAnimationFile reparsed = RenderWareAnimationFile.Parse("test.anm", edited);

        Assert.True(file.IsChanged);
        Assert.Equal(4F, reparsed.DurationSeconds, 3);
        Assert.Equal(1.5F, reparsed.Frames[2].TimeSeconds, 3);
        Assert.Equal(4F, reparsed.Frames[4].TimeSeconds, 3);
        Assert.Equal(original.Length, edited.Length);
        for (int index = 0; index < original.Length; index++)
        {
            bool editableByte = index is >= 28 and < 32 ||
                                Enumerable.Range(0, 5).Any(frame =>
                                    index >= 32 + frame * 36 && index < 36 + frame * 36);
            if (!editableByte) Assert.Equal(original[index], edited[index]);
        }
    }

    [Fact]
    public void RejectsTimeThatCrossesLinkedTrackNeighbor()
    {
        RenderWareAnimationFile file = RenderWareAnimationFile.Parse(
            "test.anm", CreateStandardAnimation());

        Assert.Throws<InvalidDataException>(() => file.SetKeyFrameTime(2, 2.1F));
        Assert.Throws<InvalidDataException>(() => file.SetKeyFrameTime(4, 0.5F));
        Assert.Throws<InvalidDataException>(() => file.ScaleToDuration(0));
    }

    [Fact]
    public void ArchivePairsCanonicalEvtAndSavesWithBackup()
    {
        Directory.CreateDirectory(_tempDirectory);
        string metPath = Path.Combine(_tempDirectory, "DATA.MET");
        CreateArchive(metPath, new[]
        {
            ("data/batting/abne/abnebat_swing.anm", CreateStandardAnimation()),
            ("data/batting/abne/abne_swing.evt", Encoding.UTF8.GetBytes(CreateEventXml())),
            ("data/batting/abne/abnebatting.dff", CreateSkeletonDff())
        });
        RenderWareAnimationArchive archive = RenderWareAnimationArchive.Load(metPath);
        RenderWareAnimationFile animation = Assert.Single(archive.Files);

        Assert.NotNull(animation.PairedEvent);
        Assert.Equal(1, archive.PairedEventCount);
        RenderWareAnimationBinding binding = Assert.IsType<RenderWareAnimationBinding>(
            archive.ResolveSkeleton(animation));
        Assert.Equal(2, binding.Skeleton.BoneCount);
        Assert.Equal(-1, binding.Skeleton.Bones[0].ParentTrackIndex);
        Assert.Equal(0, binding.Skeleton.Bones[1].ParentTrackIndex);
        IReadOnlyList<Vector3> pose = binding.SamplePose(animation, 1.5F);
        Assert.Equal(15F, pose[0].X, 3);
        Assert.Equal(125F, pose[1].X, 3);
        Assert.Equal(50F, pose[1].Y, 3);
        animation.ScaleToDuration(3F);
        AnimationSaveResult result = archive.SaveWithBackup();
        RenderWareAnimationArchive saved = RenderWareAnimationArchive.Load(metPath);

        Assert.Equal(1, result.ChangedFileCount);
        Assert.False(result.RebuiltArchive);
        Assert.True(File.Exists(result.BackupPath));
        Assert.Equal(3F, Assert.Single(saved.Files).DurationSeconds, 3);
    }

    [Fact]
    public void AnimationEditorConstructsWithItsDefaultSplitPanelSize()
    {
        Directory.CreateDirectory(_tempDirectory);
        string metPath = Path.Combine(_tempDirectory, "DATA.MET");
        CreateArchive(metPath, new[]
        {
            ("data/batting/test/test_swing.anm", CreateStandardAnimation()),
            ("data/batting/test/testbatting.dff", CreateSkeletonDff())
        });
        RenderWareAnimationArchive archive = RenderWareAnimationArchive.Load(metPath);

        Exception? failure = null;
        Thread thread = new(() =>
        {
            try
            {
                using AnimationEditorForm editor = new(archive, metPath);
                Assert.True(editor.ClientSize.Width >= editor.MinimumSize.Width);
                RenderWareAnimationFile animation = Assert.Single(archive.Files);
                RenderWareAnimationBinding binding = Assert.IsType<RenderWareAnimationBinding>(
                    archive.ResolveSkeleton(animation));
                using AnimationPosePreviewControl preview = new()
                {
                    Size = new Size(640, 300),
                    Animation = animation,
                    Binding = binding,
                    PositionSeconds = 1.5
                };
                using Bitmap bitmap = new(preview.Width, preview.Height);
                preview.DrawToBitmap(bitmap, preview.ClientRectangle);
                Assert.NotEqual(Color.Empty, bitmap.GetPixel(bitmap.Width / 2, bitmap.Height / 2));
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        Assert.Null(failure);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDirectory))
            Directory.Delete(_tempDirectory, recursive: true);
    }

    private static byte[] CreateStandardAnimation()
    {
        const int frames = 5;
        byte[] data = new byte[32 + frames * 36];
        using MemoryStream stream = new(data);
        using BinaryWriter writer = new(stream);
        WriteHeader(writer, data.Length, 1, frames, 2F);
        WriteStandardFrame(writer, 0, 0, 0, -1234);
        WriteStandardFrame(writer, 0, 100, 0, -5678);
        WriteStandardFrame(writer, 1, 10, 20, 0);
        WriteStandardFrame(writer, 1, 110, 20, 36);
        WriteStandardFrame(writer, 2, 20, 40, 72);
        return data;
    }

    private static void WriteStandardFrame(
        BinaryWriter writer, float time, float tx, float ty, int previousOffset)
    {
        writer.Write(time);
        writer.Write(0F); writer.Write(0F); writer.Write(0F); writer.Write(1F);
        writer.Write(tx); writer.Write(ty); writer.Write(0F);
        writer.Write(previousOffset);
    }

    private static byte[] CreateCompressedAnimation()
    {
        const int frames = 4;
        byte[] data = new byte[32 + frames * 22 + 24];
        using MemoryStream stream = new(data);
        using BinaryWriter writer = new(stream);
        WriteHeader(writer, data.Length, 2, frames, 1F);
        WriteCompressedFrame(writer, 0, 0, -111);
        WriteCompressedFrame(writer, 0, 0, -222);
        WriteCompressedFrame(writer, 1, 0x7800, 0);
        WriteCompressedFrame(writer, 1, 0x7800, 24);
        writer.Write(10F); writer.Write(20F); writer.Write(30F);
        writer.Write(2F); writer.Write(3F); writer.Write(4F);
        return data;
    }

    private static void WriteCompressedFrame(
        BinaryWriter writer, float time, ushort normalizedTranslation, int previousOffset)
    {
        writer.Write(time);
        writer.Write((ushort)0); writer.Write((ushort)0); writer.Write((ushort)0); writer.Write((ushort)0x7800);
        writer.Write(normalizedTranslation); writer.Write(normalizedTranslation); writer.Write(normalizedTranslation);
        writer.Write(previousOffset);
    }

    private static void WriteHeader(
        BinaryWriter writer, int totalLength, int scheme, int frames, float duration)
    {
        writer.Write(0x1B);
        writer.Write(totalLength - 12);
        writer.Write(unchecked((int)0x1803FFFF));
        writer.Write(0x100);
        writer.Write(scheme);
        writer.Write(frames);
        writer.Write(0);
        writer.Write(duration);
    }

    private static string CreateEventXml() =>
        "<?xml version=\"1.0\" encoding=\"utf-8\" ?>\r\n" +
        "<event_stream>\r\n" +
        "<classes><classdef name=\"CLASS_MOUTH\" value=\"1\">" +
        "<eventdef name=\"1\" value=\"1\"/></classdef></classes>\r\n" +
        "<event><timestamp value=\"0\"/><eventClass value=\"CLASS_MOUTH\"/>" +
        "<eventType value=\"1\"/><value value=\"1.0\"/><elementID value=\"0\"/></event>\r\n" +
        "</event_stream>\r\n";

    private static byte[] CreateSkeletonDff()
    {
        byte[] frameStructure;
        using (MemoryStream stream = new())
        using (BinaryWriter writer = new(stream))
        {
            writer.Write(2);
            WriteDffFrame(writer, -1, Vector3.Zero);
            WriteDffFrame(writer, 0, new Vector3(100, 0, 0));
            frameStructure = RwChunk(0x01, stream.ToArray());
        }

        byte[] rootHierarchy;
        using (MemoryStream stream = new())
        using (BinaryWriter writer = new(stream))
        {
            writer.Write(0x100);
            writer.Write(0);
            writer.Write(2);
            writer.Write(0);
            writer.Write(36);
            writer.Write(0); writer.Write(0); writer.Write(0);
            writer.Write(1); writer.Write(1); writer.Write(0);
            rootHierarchy = RwChunk(0x11E, stream.ToArray());
        }
        byte[] childNode;
        using (MemoryStream stream = new())
        using (BinaryWriter writer = new(stream))
        {
            writer.Write(0x100);
            writer.Write(1);
            writer.Write(0);
            childNode = RwChunk(0x11E, stream.ToArray());
        }
        byte[] frameList = RwChunk(0x0E, Combine(
            frameStructure,
            RwChunk(0x03, rootHierarchy),
            RwChunk(0x03, childNode)));
        byte[] clumpStructure = RwChunk(0x01, new byte[12]);
        return RwChunk(0x10, Combine(clumpStructure, frameList));
    }

    private static void WriteDffFrame(BinaryWriter writer, int parent, Vector3 translation)
    {
        writer.Write(1F); writer.Write(0F); writer.Write(0F);
        writer.Write(0F); writer.Write(1F); writer.Write(0F);
        writer.Write(0F); writer.Write(0F); writer.Write(1F);
        writer.Write(translation.X); writer.Write(translation.Y); writer.Write(translation.Z);
        writer.Write(parent);
        writer.Write(0);
    }

    private static byte[] RwChunk(uint id, byte[] payload)
    {
        using MemoryStream stream = new();
        using BinaryWriter writer = new(stream);
        writer.Write(id);
        writer.Write(payload.Length);
        writer.Write(unchecked((int)0x1803FFFF));
        writer.Write(payload);
        return stream.ToArray();
    }

    private static byte[] Combine(params byte[][] values)
    {
        byte[] result = new byte[values.Sum(value => value.Length)];
        int offset = 0;
        foreach (byte[] value in values)
        {
            value.CopyTo(result, offset);
            offset += value.Length;
        }
        return result;
    }

    private static void CreateArchive(string path, IReadOnlyList<(string Path, byte[] Data)> entries)
    {
        const int dataOffset = 2048;
        int totalLength = dataOffset + (entries.Count - 1) * 2048 + entries[^1].Data.Length;
        using FileStream stream = new(path, FileMode.Create, FileAccess.ReadWrite);
        using BinaryWriter writer = new(stream);
        writer.Write(dataOffset);
        writer.Write(totalLength - dataOffset);
        for (int index = 0; index < entries.Count; index++)
        {
            byte[] pathBytes = Encoding.ASCII.GetBytes(entries[index].Path);
            writer.Write(dataOffset + index * 2048);
            writer.Write(entries[index].Data.Length);
            writer.Write(pathBytes.Length);
            writer.Write(pathBytes);
        }
        writer.Write(new byte[dataOffset - checked((int)stream.Position)]);
        for (int index = 0; index < entries.Count; index++)
        {
            stream.Position = dataOffset + index * 2048;
            writer.Write(entries[index].Data);
        }
    }
}
