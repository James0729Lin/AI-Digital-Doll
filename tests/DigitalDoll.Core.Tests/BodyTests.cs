using System;
using System.IO;
using System.Linq;
using DigitalDoll.Core;
using DigitalDoll.Core.Body;
using Xunit;

namespace DigitalDoll.Core.Tests;

public sealed class BodyPackFixture
{
    public BodyPackFixture()
    {
        string dir = AppContext.BaseDirectory;
        while (!File.Exists(Path.Combine(dir, "DigitalDoll.sln")))
            dir = Path.GetDirectoryName(dir) ?? throw new DirectoryNotFoundException("repository root");
        using var stream = File.OpenRead(Path.Combine(dir, "app", "assets", "body", "body.ddbp.gz"));
        Pack = BodyPack.Load(stream);
        Engine = new MorphEngine(Pack);
    }

    public BodyPack Pack { get; }
    public MorphEngine Engine { get; }
}

public class BodyTests : IClassFixture<BodyPackFixture>
{
    private readonly BodyPackFixture _f;

    public BodyTests(BodyPackFixture fixture) => _f = fixture;

    [Fact]
    public void PackHasTheBaseMeshAndSurfaces()
    {
        Assert.Equal(19158, _f.Pack.VertexCount);
        Assert.Contains("body", _f.Pack.Surfaces.Keys);
        Assert.Contains("eyes", _f.Pack.Surfaces.Keys);
        Assert.Contains("pelvis", _f.Pack.Groups.Keys);
        Assert.DoesNotContain(_f.Pack.Targets.Keys, t => t.Contains("genitals") || t.Contains("bulge")
            || (t.Contains("nipple") && t != MorphWeights.FabricSmoothing));
        Assert.DoesNotContain(BodyCatalog.Sliders, d => d.Id.Contains("nipple") || (d.Positive ?? "").Contains("nipple"));
    }

    [Fact]
    public void SkeletonIsAHumanoidWithParentsFirst()
    {
        var names = _f.Pack.Bones.Select(b => b.Name).ToList();
        foreach (var bone in new[] { "Hips", "Spine", "Chest", "UpperChest", "Neck", "Head",
                     "LeftUpperArm", "RightLowerArm", "LeftHand", "LeftUpperLeg", "RightLowerLeg", "LeftFoot" })
            Assert.Contains(bone, names);
        for (int i = 0; i < _f.Pack.Bones.Count; i++)
            Assert.True(_f.Pack.Bones[i].Parent < i);
        Assert.Equal(-1, _f.Pack.Bones[0].Parent);
    }

    [Fact]
    public void JointsFollowTheBodyShape()
    {
        var names = _f.Pack.Bones.Select(b => b.Name).ToList();
        float Y(float[] heads, string bone) => heads[names.IndexOf(bone) * 3 + 1];
        float X(float[] heads, string bone) => heads[names.IndexOf(bone) * 3];

        var normal = _f.Engine.BoneHeads(_f.Engine.Apply(new BodyShape()));
        Assert.True(Y(normal, "Head") > Y(normal, "Neck"));
        Assert.True(Y(normal, "Neck") > Y(normal, "Hips"));
        Assert.True(Y(normal, "Hips") > Y(normal, "LeftLowerLeg"));
        Assert.True(Y(normal, "LeftLowerLeg") > Y(normal, "LeftFoot"));
        Assert.True(X(normal, "LeftUpperArm") > 0f && X(normal, "RightUpperArm") < 0f);

        var tallShape = new BodyShape();
        tallShape.Set("height", 1f);
        var tall = _f.Engine.BoneHeads(_f.Engine.Apply(tallShape));
        Assert.True(Y(tall, "Head") - Y(tall, "LeftFoot") > Y(normal, "Head") - Y(normal, "LeftFoot") + 0.05f);
    }

    [Fact]
    public void EveryDrawnVertexIsSkinned()
    {
        var w = _f.Pack.BoneWeights;
        var idx = _f.Pack.BoneIndices;
        foreach (var surface in _f.Pack.Surfaces.Values)
        {
            foreach (int v in surface.Distinct())
            {
                Assert.Equal(255, w[v * 4] + w[v * 4 + 1] + w[v * 4 + 2] + w[v * 4 + 3]);
                for (int k = 0; k < 4; k++)
                    Assert.InRange(idx[v * 4 + k], 0, _f.Pack.Bones.Count - 1);
            }
        }
    }

    [Fact]
    public void HandAndFootVerticesFollowTheirBones()
    {
        var names = _f.Pack.Bones.Select(b => b.Name).ToList();
        int Dominant(int v) => _f.Pack.BoneIndices[v * 4];
        int handBone = names.IndexOf("LeftHand"), footBone = names.IndexOf("LeftFoot"), headBone = names.IndexOf("Head");
        var p = _f.Pack.BasePositions;
        int Nearest(string group)
        {
            var (x, y, z) = _f.Engine.JointCentre(group, p);
            return _f.Pack.Surfaces["body"].Distinct().OrderBy(v =>
                (p[v * 3] - x) * (p[v * 3] - x) + (p[v * 3 + 1] - y) * (p[v * 3 + 1] - y) + (p[v * 3 + 2] - z) * (p[v * 3 + 2] - z)).First();
        }
        Assert.Equal(handBone, Dominant(Nearest("l-finger-3-3")));
        Assert.Equal(footBone, Dominant(Nearest("l-toe-3-2")));
        Assert.Equal(headBone, Dominant(Nearest("head-2")));
    }

    [Fact]
    public void DefaultBodyIsAdultHeight()
    {
        var p = _f.Engine.Apply(new BodyShape());
        var (bottom, top) = _f.Engine.VerticalExtent(p);
        Assert.InRange(top - bottom, 1.5f, 1.85f);
    }

    [Theory]
    [InlineData(0f)]
    [InlineData(1f)]
    public void EverySliderAtEitherEndUsesExistingTargets(float end)
    {
        foreach (var def in BodyCatalog.Sliders)
        {
            var shape = new BodyShape();
            float value = def.Kind == SliderKind.Bipolar && end == 0f ? -1f : end;
            foreach (var key in def.Keys)
                shape.Set(key, value);
            var missing = _f.Engine.MissingTargets(MorphWeights.Compute(shape));
            Assert.True(missing.Count == 0, $"{def.Id}={value}: missing {string.Join(", ", missing)}");
        }
    }

    [Fact]
    public void ExtremeMacrosUseExistingTargets()
    {
        foreach (var (gender, age) in new[] { (0f, 0f), (1f, 1f), (0.3f, 0.7f) })
        {
            var shape = new BodyShape();
            shape.Set("gender", gender);
            shape.Set("age", age);
            shape.Set("muscle", 1f);
            shape.Set("weight", 0f);
            shape.Set("height", 0f);
            shape.Set("proportions", 1f);
            shape.Set("breast-size", 1f);
            shape.Set("breast-firmness", 0f);
            Assert.Empty(_f.Engine.MissingTargets(MorphWeights.Compute(shape)));
        }
    }

    [Fact]
    public void EveryBodySliderChangesTheMesh()
    {
        var baseline = _f.Engine.Apply(new BodyShape());
        foreach (var def in BodyCatalog.Sliders)
        {
            var shape = new BodyShape();
            foreach (var key in def.Keys)
                shape.Set(key, def.Kind == SliderKind.Macro && def.Default >= 0.5f ? 0f : 1f);
            var moved = _f.Engine.Apply(shape);
            float maxMove = 0f;
            for (int i = 0; i < moved.Length; i++)
                maxMove = Math.Max(maxMove, Math.Abs(moved[i] - baseline[i]));
            Assert.True(maxMove > 0.002f, $"{def.Id} moved the body only {maxMove * 1000:F2} mm");
        }
    }

    [Fact]
    public void HeightSliderMakesTheBodyTaller()
    {
        float Height(float h)
        {
            var shape = new BodyShape();
            shape.Set("height", h);
            var (bottom, top) = _f.Engine.VerticalExtent(_f.Engine.Apply(shape));
            return top - bottom;
        }
        Assert.True(Height(1f) > Height(0.5f) + 0.05f);
        Assert.True(Height(0f) < Height(0.5f) - 0.05f);
    }

    [Fact]
    public void LargerCupMovesTheChestForward()
    {
        float ChestFront(float size)
        {
            var shape = new BodyShape();
            shape.Set("breast-size", size);
            var p = _f.Engine.Apply(shape);
            var chest = _f.Engine.JointCentre("spine-3", p);
            float front = float.MinValue;
            for (int v = 0; v < _f.Pack.VertexCount; v++)
            {
                if (Math.Abs(p[v * 3 + 1] - chest.Y) < 0.08f && Math.Abs(p[v * 3]) < 0.15f)
                    front = Math.Max(front, p[v * 3 + 2]);
            }
            return front;
        }
        Assert.True(ChestFront(1f) > ChestFront(0.5f) + 0.01f);
    }

    [Fact]
    public void MirroredSliderOnlyMovesOneSide()
    {
        var baseline = _f.Engine.Apply(new BodyShape());
        var shape = new BodyShape();
        shape.Set("upperarm-fat.l", 1f);
        var moved = _f.Engine.Apply(shape);
        int left = 0, right = 0;
        for (int v = 0; v < _f.Pack.VertexCount; v++)
        {
            float d = Math.Abs(moved[v * 3] - baseline[v * 3]) + Math.Abs(moved[v * 3 + 2] - baseline[v * 3 + 2]);
            if (d < 1e-4f)
                continue;
            if (baseline[v * 3] > 0)
                left++;
            else
                right++;
        }
        Assert.True(left > 50);
        Assert.Equal(0, right);
    }

    [Fact]
    public void UnderwearCoversChestAndHipsButNotFaceHandsOrFeet()
    {
        var cut = UnderwearCut.Compute(_f.Pack);
        var p = _f.Pack.BasePositions;
        (float X, float Y) Joint(string name) => (
            _f.Pack.Groups[name].Average(v => p[v * 3]),
            _f.Pack.Groups[name].Average(v => p[v * 3 + 1]));

        Assert.True(cut.TopLow < cut.TopHigh && cut.ShortsLow < cut.ShortsHigh);
        Assert.True(cut.TopLow > cut.ShortsHigh, "the top and the shorts should not overlap");
        Assert.True(cut.Covers(0f, (cut.TopLow + cut.TopHigh) / 2f));
        Assert.True(cut.Covers(0f, Joint("pelvis").Y));
        Assert.False(cut.Covers(0f, Joint("head").Y));
        Assert.False(cut.Covers(Joint("l-knee").X, Joint("l-knee").Y));
        Assert.False(cut.Covers(Joint("l-hand").X, Joint("l-hand").Y));
        Assert.False(cut.Covers(Joint("l-ankle").X, Joint("l-ankle").Y));
        Assert.False(cut.Covers(0f, (cut.TopLow + cut.TopHigh) / 2f, wearsTop: false));
    }

    [Fact]
    public void OnlyMaleBodiesSkipTheTop()
    {
        var shape = new BodyShape();
        Assert.True(UnderwearCut.WearsTop(shape));
        shape.Set("gender", 0.5f);
        Assert.True(UnderwearCut.WearsTop(shape));
        shape.Set("gender", 1f);
        Assert.False(UnderwearCut.WearsTop(shape));
    }

    [Fact]
    public void ThreeWayWeightsSumToOne()
    {
        foreach (float v in new[] { 0f, 0.2f, 0.5f, 0.8f, 1f })
            Assert.Equal(1f, MorphWeights.Three(v).Sum(), 4);
    }

    [Fact]
    public void RaceWeightsAreNormalised()
    {
        var shape = new BodyShape();
        shape.Set("asian", 1f);
        shape.Set("african", 0f);
        shape.Set("caucasian", 0f);
        var w = MorphWeights.Compute(shape);
        Assert.Equal(1f, w["macrodetails/asian-female-young"], 4);
        Assert.False(w.ContainsKey("macrodetails/african-female-young"));
    }
}

public class BodyShapeTests
{
    [Fact]
    public void DefaultsAreNotStoredAndValuesAreClamped()
    {
        var shape = new BodyShape();
        shape.Set("height", 0.5f);
        Assert.Empty(shape.Values);
        shape.Set("buttocks", 3f);
        Assert.Equal(1f, shape.Get("buttocks"));
        shape.Set("gender", -2f);
        Assert.Equal(0f, shape.Get("gender"));
    }

    [Fact]
    public void UnknownSliderIsRejected()
    {
        Assert.Throws<ArgumentException>(() => new BodyShape().Set("nipple-size", 1f));
    }

    [Fact]
    public void JsonRoundTripKeepsEverything()
    {
        var shape = new BodyShape { SkinColor = "#C08060", UnderwearColor = "#FFFFFF" };
        shape.Set("gender", 1f);
        shape.Set("upperleg-fat.r", -0.4f);
        shape.Set("shape-fem-full-hourglass", 0.7f);

        var copy = BodyShape.FromJson(shape.ToJson());
        Assert.True(copy.SameAs(shape));
        Assert.Equal(-0.4f, copy.Get("upperleg-fat.r"), 4);
    }

    [Fact]
    public void UnknownSlidersInFilesAreIgnored()
    {
        var shape = BodyShape.FromJson("{\"formatVersion\":1,\"body\":{\"removed-slider\":0.5,\"weight\":0.9}}");
        Assert.Equal(0.9f, shape.Get("weight"), 4);
    }

    [Fact]
    public void CloneIsIndependent()
    {
        var shape = new BodyShape();
        var copy = shape.Clone();
        copy.Set("weight", 1f);
        Assert.False(copy.SameAs(shape));
        Assert.Equal(0.5f, shape.Get("weight"));
    }
}

public class AppSettingsTests
{
    [Fact]
    public void RoundTripsTheCharacterChoice()
    {
        var settings = new AppSettings { Character = CharacterSource.Body };
        Assert.Contains("\"body\"", settings.ToJson());
        Assert.Equal(CharacterSource.Body, AppSettings.FromJson(settings.ToJson()).Character);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("{\"character\":\"robot\"}")]
    public void BrokenFilesGiveDefaults(string? json)
    {
        Assert.Equal(CharacterSource.Default, AppSettings.FromJson(json).Character);
    }
}
