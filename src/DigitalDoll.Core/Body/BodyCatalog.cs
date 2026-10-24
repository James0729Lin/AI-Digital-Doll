using System.Collections.Generic;
using System.Linq;

namespace DigitalDoll.Core.Body;

public enum SliderKind
{
    /// <summary>0..1 parameter interpolated across macro targets (gender, weight…).</summary>
    Macro,

    /// <summary>-1..1 between a "decrease" and an "increase" target.</summary>
    Bipolar,

    /// <summary>0..1 on a single target.</summary>
    Unipolar,
}

/// <summary>A tab of sliders; <see cref="Section"/> is "body" or "face".</summary>
public sealed record BodyGroup(string Id, string Zh, string En, string Section = "body");

/// <summary>
/// One slider of the body editor. Mirrored sliders exist once per side; their target
/// names contain <c>{side}</c> (MakeHuman's "l" / "r", from the character's view).
/// </summary>
public sealed record SliderDef(
    string Id,
    string Group,
    SliderKind Kind,
    string Zh,
    string En,
    bool Basic,
    float Default = 0f,
    string? Negative = null,
    string? Positive = null,
    bool Mirrored = false)
{
    public float Min => Kind == SliderKind.Bipolar ? -1f : 0f;
    public float Max => 1f;

    public IEnumerable<string> Keys => Mirrored ? new[] { Id + ".l", Id + ".r" } : new[] { Id };

    public string TargetName(string pattern, string? side) =>
        side == null ? pattern : pattern.Replace("{side}", side);
}

/// <summary>Every body slider, grouped the way the editor presents them.</summary>
public static class BodyCatalog
{
    public static readonly IReadOnlyList<BodyGroup> Groups = new[]
    {
        new BodyGroup("overall", "整體", "Overall"),
        new BodyGroup("chest", "胸", "Chest"),
        new BodyGroup("waist", "腰・腹", "Waist"),
        new BodyGroup("torso", "軀幹", "Torso"),
        new BodyGroup("hips", "臀・屁股", "Hips"),
        new BodyGroup("arms", "手臂", "Arms"),
        new BodyGroup("legs", "腿", "Legs"),
        new BodyGroup("shapes", "體型範本", "Body types"),
        new BodyGroup("face", "臉型", "Face", "face"),
        new BodyGroup("eyes", "眼・眉", "Eyes", "face"),
        new BodyGroup("nose", "鼻子", "Nose", "face"),
        new BodyGroup("mouth", "嘴巴", "Mouth", "face"),
        new BodyGroup("ears", "耳朵", "Ears", "face"),
    };

    public static readonly IReadOnlyList<SliderDef> Sliders = Build();

    public static readonly IReadOnlyDictionary<string, SliderDef> ById = Sliders.ToDictionary(s => s.Id);

    private static List<SliderDef> Build()
    {
        var list = new List<SliderDef>
        {
            Macro("gender", "overall", "性別（女 ↔ 男）", "Gender (female ↔ male)", true, 0f),
            Macro("age", "overall", "年齡（25 ↔ 90 歲）", "Age (25 ↔ 90)", true, 0f),
            Macro("height", "overall", "身高", "Height", true, 0.5f),
            Macro("weight", "overall", "體重", "Weight", true, 0.5f),
            Macro("muscle", "overall", "肌肉量", "Muscle", true, 0.5f),
            Macro("proportions", "overall", "身材比例（一般 ↔ 理想）", "Proportions (common ↔ ideal)", true, 0.5f),
            Macro("african", "overall", "族裔特徵：非洲", "Ethnicity: African", false, 1f / 3f),
            Macro("asian", "overall", "族裔特徵：亞洲", "Ethnicity: Asian", false, 1f / 3f),
            Macro("caucasian", "overall", "族裔特徵：歐洲", "Ethnicity: European", false, 1f / 3f),

            Macro("breast-size", "chest", "胸部大小", "Breast size", true, 0.5f),
            Macro("breast-firmness", "chest", "胸部緊實度（垂 ↔ 挺）", "Breast firmness", true, 0.5f),
            Pair("breast-position", "chest", "胸部位置（下 ↔ 上）", "Breast position (down ↔ up)", true, "breast/breast-trans-down", "breast/breast-trans-up"),
            Pair("breast-distance", "chest", "左右間距", "Breast spacing", false, "breast/breast-dist-decr", "breast/breast-dist-incr"),
            Pair("breast-point", "chest", "尖挺度", "Breast projection", false, "breast/breast-point-decr", "breast/breast-point-incr"),
            Pair("breast-volume-vert", "chest", "上下飽滿度", "Upper / lower fullness", false, "breast/breast-volume-vert-down", "breast/breast-volume-vert-up"),
            Measure("bust-circ", "chest", "胸圍", "Bust", true),
            Measure("underbust-circ", "chest", "下胸圍", "Underbust", false),
            Measure("frontchest-dist", "chest", "胸前寬", "Front chest width", false),
            Pair("pectoral", "chest", "胸大肌", "Pectoral muscle", false, "torso/torso-muscle-pectoral-decr", "torso/torso-muscle-pectoral-incr"),

            Measure("waist-circ", "waist", "腰圍", "Waist", true),
            Pair("waist-height", "waist", "腰線高低", "Waistline height", true, "hip/hip-waist-down", "hip/hip-waist-up"),
            Pair("stomach-tone", "waist", "腹部（凸出 ↔ 平坦）", "Stomach (soft ↔ flat)", true, "stomach/stomach-tone-decr", "stomach/stomach-tone-incr"),
            Pair("navel-depth", "waist", "肚臍深淺", "Navel depth", false, "stomach/stomach-navel-in", "stomach/stomach-navel-out"),
            Pair("navel-height", "waist", "肚臍位置", "Navel height", false, "stomach/stomach-navel-down", "stomach/stomach-navel-up"),
            Measure("napetowaist-dist", "waist", "後頸到腰長度", "Nape to waist", false),
            Measure("waisttohip-dist", "waist", "腰到臀長度", "Waist to hip", false),

            Measure("shoulder-dist", "torso", "肩寬", "Shoulder width", true),
            Pair("torso-vshape", "torso", "V 字型（肩寬腰窄）", "V shape", true, "torso/torso-vshape-decr", "torso/torso-vshape-incr"),
            Pair("torso-width", "torso", "軀幹寬度", "Torso width", true, "torso/torso-scale-horiz-decr", "torso/torso-scale-horiz-incr"),
            Pair("torso-depth", "torso", "軀幹厚度", "Torso depth", false, "torso/torso-scale-depth-decr", "torso/torso-scale-depth-incr"),
            Pair("torso-length", "torso", "軀幹長度", "Torso length", false, "torso/torso-scale-vert-decr", "torso/torso-scale-vert-incr"),
            Pair("dorsi", "torso", "背闊肌", "Lats", false, "torso/torso-muscle-dorsi-decr", "torso/torso-muscle-dorsi-incr"),
            Pair("torso-move-x", "torso", "軀幹左右位移", "Torso shift (in ↔ out)", false, "torso/torso-trans-in", "torso/torso-trans-out"),
            Pair("torso-move-y", "torso", "軀幹上下位移", "Torso shift (down ↔ up)", false, "torso/torso-trans-down", "torso/torso-trans-up"),
            Pair("torso-move-z", "torso", "軀幹前後位移", "Torso shift (back ↔ forward)", false, "torso/torso-trans-backward", "torso/torso-trans-forward"),
            Measure("neck-circ", "torso", "脖子粗細", "Neck", false),
            Measure("neck-height", "torso", "脖子長度", "Neck length", false),

            Measure("hips-circ", "hips", "臀圍", "Hips", true),
            Pair("hip-width", "hips", "臀部寬度", "Hip width", true, "hip/hip-scale-horiz-decr", "hip/hip-scale-horiz-incr"),
            Pair("buttocks", "hips", "屁股豐滿度", "Buttocks volume", true, "buttocks/buttocks-volume-decr", "buttocks/buttocks-volume-incr"),
            Pair("hip-depth", "hips", "臀部厚度", "Hip depth", false, "hip/hip-scale-depth-decr", "hip/hip-scale-depth-incr"),
            Pair("hip-height", "hips", "臀部高度", "Hip height", false, "hip/hip-scale-vert-decr", "hip/hip-scale-vert-incr"),
            Pair("pelvis-tone", "hips", "骨盆緊實度", "Pelvis tone", false, "pelvis/pelvis-tone-decr", "pelvis/pelvis-tone-incr"),
            Pair("hip-move-x", "hips", "臀部左右位移", "Hip shift (in ↔ out)", false, "hip/hip-trans-in", "hip/hip-trans-out"),
            Pair("hip-move-y", "hips", "臀部上下位移", "Hip shift (down ↔ up)", false, "hip/hip-trans-down", "hip/hip-trans-up"),
            Pair("hip-move-z", "hips", "臀部前後位移", "Hip shift (back ↔ forward)", false, "hip/hip-trans-backward", "hip/hip-trans-forward"),
        };

        list.AddRange(new[]
        {
            Side("upperarm-fat", "arms", "上臂粗細", "Upper arm fat", true),
            Side("upperarm-muscle", "arms", "上臂肌肉", "Upper arm muscle", true),
            Side("upperarm-shoulder-muscle", "arms", "肩部肌肉", "Shoulder muscle", false),
            Side("upperarm-scale-horiz", "arms", "上臂寬度", "Upper arm width", false),
            Side("upperarm-scale-depth", "arms", "上臂厚度", "Upper arm depth", false),
            Side("upperarm-scale-vert", "arms", "上臂長度", "Upper arm length", false),
            Side("lowerarm-fat", "arms", "前臂粗細", "Forearm fat", true),
            Side("lowerarm-muscle", "arms", "前臂肌肉", "Forearm muscle", false),
            Side("lowerarm-scale-horiz", "arms", "前臂寬度", "Forearm width", false),
            Side("lowerarm-scale-depth", "arms", "前臂厚度", "Forearm depth", false),
            Side("lowerarm-scale-vert", "arms", "前臂長度", "Forearm length", false),
            Side("hand-scale", "arms", "手的大小", "Hand size", false),
        });
        list.AddRange(new[]
        {
            Measure("upperarm-circ", "arms", "上臂圍", "Upper arm girth", true),
            Measure("upperarm-length", "arms", "上臂長", "Upper arm length (measured)", false),
            Measure("lowerarm-length", "arms", "前臂長", "Forearm length (measured)", false),
            Measure("wrist-circ", "arms", "手腕圍", "Wrist", false),
        });

        list.AddRange(new[]
        {
            Pair("upperlegs-height", "legs", "大腿長度", "Thigh length", true, "armslegs/upperlegs-height-decr", "armslegs/upperlegs-height-incr"),
            Pair("lowerlegs-height", "legs", "小腿長度", "Calf length", true, "armslegs/lowerlegs-height-decr", "armslegs/lowerlegs-height-incr"),
            Side("upperleg-fat", "legs", "大腿粗細", "Thigh fat", true),
            Side("upperleg-muscle", "legs", "大腿肌肉", "Thigh muscle", true),
            Side("upperleg-scale-horiz", "legs", "大腿寬度", "Thigh width", false),
            Side("upperleg-scale-depth", "legs", "大腿厚度", "Thigh depth", false),
            Side("upperleg-scale-vert", "legs", "大腿縱向比例", "Thigh vertical scale", false),
            Side("lowerleg-fat", "legs", "小腿粗細", "Calf fat", true),
            Side("lowerleg-muscle", "legs", "小腿肌肉", "Calf muscle", false),
            Side("lowerleg-scale-horiz", "legs", "小腿寬度", "Calf width", false),
            Side("lowerleg-scale-depth", "legs", "小腿厚度", "Calf depth", false),
            Side("lowerleg-scale-vert", "legs", "小腿縱向比例", "Calf vertical scale", false),
            Side("leg-valgus", "legs", "O 型腿 ↔ X 型腿", "Bow legs ↔ knock knees", false),
            Side("foot-scale", "legs", "腳的大小", "Foot size", false),
            Measure("thigh-circ", "legs", "大腿圍", "Thigh girth", true),
            Measure("calf-circ", "legs", "小腿圍", "Calf girth", false),
            Measure("knee-circ", "legs", "膝圍", "Knee girth", false),
            Measure("ankle-circ", "legs", "腳踝圍", "Ankle girth", false),
            Measure("upperleg-height", "legs", "大腿長（測量）", "Thigh length (measured)", false),
            Measure("lowerleg-height", "legs", "小腿長（測量）", "Calf length (measured)", false),
        });

        var shapes = new (string Id, string Zh, string En)[]
        {
            ("fem-full-hourglass", "女：豐滿沙漏型", "Female: full hourglass"),
            ("fem-neat-hourglass", "女：纖細沙漏型", "Female: neat hourglass"),
            ("fem-triangle", "女：梨型", "Female: pear"),
            ("fem-invert-triangle", "女：倒三角型", "Female: inverted triangle"),
            ("fem-rectangle", "女：直筒型", "Female: rectangle"),
            ("fem-lean-column", "女：纖瘦柱型", "Female: lean column"),
            ("fem-apple", "女：蘋果型", "Female: apple"),
            ("fem-diamond", "女：菱形", "Female: diamond"),
            ("fem-adrenal", "女：上身厚實型", "Female: adrenal"),
            ("fem-liver", "女：中段豐腴型", "Female: liver"),
            ("fem-ovary", "女：下身豐腴型", "Female: ovary"),
            ("fem-thyroid", "女：修長型", "Female: thyroid"),
            ("man-trapezoid", "男：梯形", "Male: trapezoid"),
            ("man-invert-triangle", "男：倒三角型", "Male: inverted triangle"),
            ("man-rectangle", "男：直筒型", "Male: rectangle"),
            ("man-triangle", "男：梨型", "Male: triangle"),
            ("man-lean-column", "男：纖瘦柱型", "Male: lean column"),
            ("man-apple", "男：蘋果型", "Male: apple"),
            ("man-adrenal", "男：上身厚實型", "Male: adrenal"),
            ("man-liver", "男：中段豐腴型", "Male: liver"),
            ("man-gonadal", "男：下身豐腴型", "Male: gonadal"),
            ("man-thyroid", "男：修長型", "Male: thyroid"),
        };
        foreach (var (id, zh, en) in shapes)
            list.Add(new SliderDef("shape-" + id, "shapes", SliderKind.Unipolar, zh, en, true,
                Positive: "bodyshapes/bodyshapes-elvs-" + id));

        AddFace(list);
        return list;
    }

    private static void AddFace(List<SliderDef> list)
    {
        var headShapes = new (string Id, string Zh, string En)[]
        {
            ("oval", "臉型：鵝蛋臉", "Shape: oval"),
            ("round", "臉型：圓臉", "Shape: round"),
            ("square", "臉型：方臉", "Shape: square"),
            ("rectangular", "臉型：長臉", "Shape: long"),
            ("triangular", "臉型：三角臉", "Shape: triangular"),
            ("invertedtriangular", "臉型：倒三角（瓜子臉）", "Shape: inverted triangle"),
            ("diamond", "臉型：菱形臉", "Shape: diamond"),
        };
        foreach (var (id, zh, en) in headShapes)
            list.Add(new SliderDef("head-" + id, "face", SliderKind.Unipolar, zh, en, true, Positive: $"head/head-{id}"));

        list.AddRange(new[]
        {
            Pair("head-fat", "face", "臉部胖瘦", "Face fullness", true, "head/head-fat-decr", "head/head-fat-incr"),
            Pair("head-scale-horiz", "face", "臉寬", "Face width", true, "head/head-scale-horiz-decr", "head/head-scale-horiz-incr"),
            Pair("head-scale-vert", "face", "臉長", "Face length", true, "head/head-scale-vert-decr", "head/head-scale-vert-incr"),
            Pair("head-scale-depth", "face", "頭部前後深度", "Head depth", false, "head/head-scale-depth-decr", "head/head-scale-depth-incr"),
            Pair("head-back", "face", "後腦勺", "Back of head", false, "head/head-back-scale-depth-decr", "head/head-back-scale-depth-incr"),
            Pair("head-age", "face", "臉部年齡感", "Facial age", false, "head/head-age-decr", "head/head-age-incr"),
            Pair("head-angle", "face", "頭部傾斜", "Head tilt", false, "head/head-angle-in", "head/head-angle-out"),
            Pair("chin-width", "face", "下巴寬度", "Chin width", true, "chin/chin-width-decr", "chin/chin-width-incr"),
            Pair("chin-height", "face", "下巴長度", "Chin height", true, "chin/chin-height-decr", "chin/chin-height-incr"),
            Pair("chin-prominent", "face", "下巴突出", "Chin projection", false, "chin/chin-prominent-decr", "chin/chin-prominent-incr"),
            Pair("chin-bones", "face", "下顎骨", "Jaw bones", true, "chin/chin-bones-decr", "chin/chin-bones-incr"),
            Pair("chin-jaw-drop", "face", "下顎角", "Jaw angle", false, "chin/chin-jaw-drop-decr", "chin/chin-jaw-drop-incr"),
            Pair("chin-prognathism", "face", "戽斗", "Underbite", false, "chin/chin-prognathism-decr", "chin/chin-prognathism-incr"),
            Pair("chin-cleft", "face", "下巴凹痕", "Chin cleft", false, "chin/chin-cleft-decr", "chin/chin-cleft-incr"),
            Mirror("cheek-volume", "face", "臉頰豐滿", "Cheek volume", true, "cheek/{side}-cheek-volume-decr", "cheek/{side}-cheek-volume-incr"),
            Mirror("cheek-bones", "face", "顴骨", "Cheekbones", true, "cheek/{side}-cheek-bones-decr", "cheek/{side}-cheek-bones-incr"),
            Mirror("cheek-inner", "face", "臉頰內側", "Inner cheek", false, "cheek/{side}-cheek-inner-decr", "cheek/{side}-cheek-inner-incr"),
            Mirror("cheek-height", "face", "臉頰高低", "Cheek height", false, "cheek/{side}-cheek-trans-down", "cheek/{side}-cheek-trans-up"),
            Pair("forehead-height", "face", "額頭高度", "Forehead height", true, "forehead/forehead-scale-vert-decr", "forehead/forehead-scale-vert-incr"),
            Pair("forehead-slope", "face", "額頭前後", "Forehead slope", false, "forehead/forehead-trans-backward", "forehead/forehead-trans-forward"),
            Pair("forehead-nubian", "face", "額頭弧度", "Forehead curve", false, "forehead/forehead-nubian-decr", "forehead/forehead-nubian-incr"),
            Pair("forehead-temple", "face", "太陽穴", "Temples", false, "forehead/forehead-temple-decr", "forehead/forehead-temple-incr"),
            Pair("neck-width", "face", "脖子寬度", "Neck width", false, "neck/neck-scale-horiz-decr", "neck/neck-scale-horiz-incr"),
            Pair("neck-double", "face", "雙下巴", "Double chin", false, "neck/neck-double-decr", "neck/neck-double-incr"),

            Mirror("eye-scale", "eyes", "眼睛大小", "Eye size", true, "eyes/{side}-eye-scale-decr", "eyes/{side}-eye-scale-incr"),
            Mirror("eye-height", "eyes", "眼睛位置（下 ↔ 上）", "Eye position (down ↔ up)", true, "eyes/{side}-eye-trans-down", "eyes/{side}-eye-trans-up"),
            Mirror("eye-spacing", "eyes", "眼距（近 ↔ 遠）", "Eye spacing", true, "eyes/{side}-eye-trans-in", "eyes/{side}-eye-trans-out"),
            Mirror("eye-corner-outer", "eyes", "眼尾（下垂 ↔ 上揚）", "Outer corner (down ↔ up)", true, "eyes/{side}-eye-corner2-down", "eyes/{side}-eye-corner2-up"),
            Mirror("eye-corner-inner", "eyes", "內眼角（下 ↔ 上）", "Inner corner", false, "eyes/{side}-eye-corner1-down", "eyes/{side}-eye-corner1-up"),
            Mirror("eye-open", "eyes", "眼睛睜開程度", "Eye opening", true, "eyes/{side}-eye-height2-decr", "eyes/{side}-eye-height2-incr"),
            Mirror("eye-height1", "eyes", "上眼瞼內側", "Upper lid (inner)", false, "eyes/{side}-eye-height1-decr", "eyes/{side}-eye-height1-incr"),
            Mirror("eye-height3", "eyes", "上眼瞼外側", "Upper lid (outer)", false, "eyes/{side}-eye-height3-decr", "eyes/{side}-eye-height3-incr"),
            Mirror("eye-epicanthus", "eyes", "內眥贅皮", "Epicanthic fold", false, "eyes/{side}-eye-epicanthus-in", "eyes/{side}-eye-epicanthus-out"),
            Mirror("eye-fold", "eyes", "眼皮摺痕（內凹 ↔ 外凸）", "Eyelid fold", false, "eyes/{side}-eye-eyefold-concave", "eyes/{side}-eye-eyefold-convex"),
            Mirror("eye-fold-height", "eyes", "雙眼皮高度", "Eyelid crease height", false, "eyes/{side}-eye-eyefold-down", "eyes/{side}-eye-eyefold-up"),
            Mirror("eye-fold-angle", "eyes", "眼皮角度", "Eyelid angle", false, "eyes/{side}-eye-eyefold-angle-down", "eyes/{side}-eye-eyefold-angle-up"),
            Mirror("eye-bag", "eyes", "眼袋", "Eye bags", false, "eyes/{side}-eye-bag-decr", "eyes/{side}-eye-bag-incr"),
            Mirror("eye-bag-depth", "eyes", "眼袋凹凸", "Eye bag depth", false, "eyes/{side}-eye-bag-in", "eyes/{side}-eye-bag-out"),
            Mirror("eye-depth", "eyes", "眼窩深淺", "Eye depth", false, "eyes/{side}-eye-push1-in", "eyes/{side}-eye-push1-out"),
            Pair("brow-height", "eyes", "眉毛高低", "Brow height", true, "eyebrows/eyebrows-trans-down", "eyebrows/eyebrows-trans-up"),
            Pair("brow-angle", "eyes", "眉毛角度（下垂 ↔ 上揚）", "Brow angle", true, "eyebrows/eyebrows-angle-down", "eyebrows/eyebrows-angle-up"),
            Pair("brow-depth", "eyes", "眉骨", "Brow ridge", false, "eyebrows/eyebrows-trans-backward", "eyebrows/eyebrows-trans-forward"),

            Pair("nose-width", "nose", "鼻子寬度", "Nose width", true, "nose/nose-scale-horiz-decr", "nose/nose-scale-horiz-incr"),
            Pair("nose-length", "nose", "鼻子長度", "Nose length", true, "nose/nose-scale-vert-decr", "nose/nose-scale-vert-incr"),
            Pair("nose-depth", "nose", "鼻子高挺", "Nose projection", true, "nose/nose-scale-depth-decr", "nose/nose-scale-depth-incr"),
            Pair("nose-tip-width", "nose", "鼻頭大小", "Tip width", true, "nose/nose-point-width-decr", "nose/nose-point-width-incr"),
            Pair("nose-tip", "nose", "鼻尖（下 ↔ 上翹）", "Tip (down ↔ up)", true, "nose/nose-point-down", "nose/nose-point-up"),
            Pair("nose-nostrils", "nose", "鼻翼寬度", "Nostril width", true, "nose/nose-nostrils-width-decr", "nose/nose-nostrils-width-incr"),
            Pair("nose-position", "nose", "鼻子位置（下 ↔ 上）", "Nose position", false, "nose/nose-trans-down", "nose/nose-trans-up"),
            Pair("nose-forward", "nose", "鼻子前後", "Nose forward", false, "nose/nose-trans-backward", "nose/nose-trans-forward"),
            Pair("nose-side", "nose", "鼻子左右", "Nose shift", false, "nose/nose-trans-in", "nose/nose-trans-out"),
            Pair("nose-hump", "nose", "鼻樑駝峰", "Hump", false, "nose/nose-hump-decr", "nose/nose-hump-incr"),
            Pair("nose-curve", "nose", "鼻樑曲線（凹 ↔ 凸）", "Bridge curve", false, "nose/nose-curve-concave", "nose/nose-curve-convex"),
            Pair("nose-greek", "nose", "希臘鼻", "Greek nose", false, "nose/nose-greek-decr", "nose/nose-greek-incr"),
            Pair("nose-volume", "nose", "鼻子份量", "Nose volume", false, "nose/nose-volume-decr", "nose/nose-volume-incr"),
            Pair("nose-base", "nose", "鼻底高低", "Nose base", false, "nose/nose-base-down", "nose/nose-base-up"),
            Pair("nose-width1", "nose", "鼻根寬度", "Bridge width (top)", false, "nose/nose-width1-decr", "nose/nose-width1-incr"),
            Pair("nose-width2", "nose", "鼻樑中段寬度", "Bridge width (middle)", false, "nose/nose-width2-decr", "nose/nose-width2-incr"),
            Pair("nose-width3", "nose", "鼻樑下段寬度", "Bridge width (low)", false, "nose/nose-width3-decr", "nose/nose-width3-incr"),
            Pair("nose-nostril-angle", "nose", "鼻孔角度", "Nostril angle", false, "nose/nose-nostrils-angle-down", "nose/nose-nostrils-angle-up"),
            Pair("nose-septum", "nose", "鼻中隔角度", "Septum angle", false, "nose/nose-septumangle-decr", "nose/nose-septumangle-incr"),
            Pair("nose-flaring", "nose", "鼻翼外擴", "Nostril flare", false, "nose/nose-flaring-decr", "nose/nose-flaring-incr"),
            Pair("nose-compression", "nose", "鼻子壓縮", "Compression", false, "nose/nose-compression-compress", "nose/nose-compression-uncompress"),

            Pair("mouth-width", "mouth", "嘴巴寬度", "Mouth width", true, "mouth/mouth-scale-horiz-decr", "mouth/mouth-scale-horiz-incr"),
            Pair("mouth-height", "mouth", "嘴巴高度", "Mouth height", false, "mouth/mouth-scale-vert-decr", "mouth/mouth-scale-vert-incr"),
            Pair("mouth-position", "mouth", "嘴巴位置（下 ↔ 上）", "Mouth position", true, "mouth/mouth-trans-down", "mouth/mouth-trans-up"),
            Pair("mouth-forward", "mouth", "嘴巴前後", "Mouth forward", false, "mouth/mouth-trans-backward", "mouth/mouth-trans-forward"),
            Pair("mouth-depth", "mouth", "嘴巴深度", "Mouth depth", false, "mouth/mouth-scale-depth-decr", "mouth/mouth-scale-depth-incr"),
            Pair("mouth-corners", "mouth", "嘴角（下垂 ↔ 上揚）", "Corners (down ↔ up)", true, "mouth/mouth-angles-down", "mouth/mouth-angles-up"),
            Pair("upperlip-volume", "mouth", "上唇厚度", "Upper lip fullness", true, "mouth/mouth-upperlip-volume-decr", "mouth/mouth-upperlip-volume-incr"),
            Pair("lowerlip-volume", "mouth", "下唇厚度", "Lower lip fullness", true, "mouth/mouth-lowerlip-volume-decr", "mouth/mouth-lowerlip-volume-incr"),
            Pair("cupidsbow", "mouth", "唇峰", "Cupid's bow", false, "mouth/mouth-cupidsbow-decr", "mouth/mouth-cupidsbow-incr"),
            Pair("cupidsbow-width", "mouth", "唇峰寬度", "Cupid's bow width", false, "mouth/mouth-cupidsbow-width-decr", "mouth/mouth-cupidsbow-width-incr"),
            Pair("upperlip-height", "mouth", "上唇高度", "Upper lip height", false, "mouth/mouth-upperlip-height-decr", "mouth/mouth-upperlip-height-incr"),
            Pair("lowerlip-height", "mouth", "下唇高度", "Lower lip height", false, "mouth/mouth-lowerlip-height-decr", "mouth/mouth-lowerlip-height-incr"),
            Pair("upperlip-width", "mouth", "上唇寬度", "Upper lip width", false, "mouth/mouth-upperlip-width-decr", "mouth/mouth-upperlip-width-incr"),
            Pair("lowerlip-width", "mouth", "下唇寬度", "Lower lip width", false, "mouth/mouth-lowerlip-width-decr", "mouth/mouth-lowerlip-width-incr"),
            Pair("upperlip-middle", "mouth", "上唇中央", "Upper lip middle", false, "mouth/mouth-upperlip-middle-down", "mouth/mouth-upperlip-middle-up"),
            Pair("lowerlip-middle", "mouth", "下唇中央", "Lower lip middle", false, "mouth/mouth-lowerlip-middle-down", "mouth/mouth-lowerlip-middle-up"),
            Pair("upperlip-ext", "mouth", "上唇外側", "Upper lip sides", false, "mouth/mouth-upperlip-ext-down", "mouth/mouth-upperlip-ext-up"),
            Pair("lowerlip-ext", "mouth", "下唇外側", "Lower lip sides", false, "mouth/mouth-lowerlip-ext-down", "mouth/mouth-lowerlip-ext-up"),
            Pair("philtrum", "mouth", "人中", "Philtrum", false, "mouth/mouth-philtrum-volume-decr", "mouth/mouth-philtrum-volume-incr"),
            Pair("dimples", "mouth", "酒窩", "Dimples", true, "mouth/mouth-dimples-in", "mouth/mouth-dimples-out"),
            Pair("laugh-lines", "mouth", "法令紋", "Laugh lines", false, "mouth/mouth-laugh-lines-in", "mouth/mouth-laugh-lines-out"),

            Mirror("ear-scale", "ears", "耳朵大小", "Ear size", true, "ears/{side}-ear-scale-decr", "ears/{side}-ear-scale-incr"),
            Mirror("ear-pointed", "ears", "耳朵形狀（尖 ↔ 三角）", "Shape (pointed ↔ triangle)", true, "ears/{side}-ear-shape-pointed", "ears/{side}-ear-shape-triangle"),
            Mirror("ear-round", "ears", "耳朵形狀（方 ↔ 圓）", "Shape (square ↔ round)", false, "ears/{side}-ear-shape-square", "ears/{side}-ear-shape-round"),
            Mirror("ear-flap", "ears", "招風耳", "Protruding", true, "ears/{side}-ear-flap-decr", "ears/{side}-ear-flap-incr"),
            Mirror("ear-height", "ears", "耳朵高低", "Ear height", false, "ears/{side}-ear-trans-down", "ears/{side}-ear-trans-up"),
            Mirror("ear-forward", "ears", "耳朵前後", "Ear position", false, "ears/{side}-ear-trans-backward", "ears/{side}-ear-trans-forward"),
            Mirror("ear-length", "ears", "耳朵長度", "Ear length", false, "ears/{side}-ear-scale-vert-decr", "ears/{side}-ear-scale-vert-incr"),
            Mirror("ear-depth", "ears", "耳朵厚度", "Ear depth", false, "ears/{side}-ear-scale-depth-decr", "ears/{side}-ear-scale-depth-incr"),
            Mirror("ear-lobe", "ears", "耳垂", "Ear lobe", false, "ears/{side}-ear-lobe-decr", "ears/{side}-ear-lobe-incr"),
            Mirror("ear-rot", "ears", "耳朵角度", "Ear rotation", false, "ears/{side}-ear-rot-backward", "ears/{side}-ear-rot-forward"),
            Mirror("ear-wing", "ears", "耳輪", "Ear rim", false, "ears/{side}-ear-wing-decr", "ears/{side}-ear-wing-incr"),
        });
    }

    private static SliderDef Mirror(string id, string group, string zh, string en, bool basic, string negative, string positive) =>
        new(id, group, SliderKind.Bipolar, zh, en, basic, 0f, negative, positive, Mirrored: true);

    private static SliderDef Macro(string id, string group, string zh, string en, bool basic, float value) =>
        new(id, group, SliderKind.Macro, zh, en, basic, value);

    private static SliderDef Pair(string id, string group, string zh, string en, bool basic, string negative, string positive) =>
        new(id, group, SliderKind.Bipolar, zh, en, basic, 0f, negative, positive);

    private static SliderDef Measure(string id, string group, string zh, string en, bool basic) =>
        Pair("measure-" + id, group, zh, en, basic, $"measure/measure-{id}-decr", $"measure/measure-{id}-incr");

    private static SliderDef Side(string id, string group, string zh, string en, bool basic) =>
        new(id, group, SliderKind.Bipolar, zh, en, basic, 0f,
            $"armslegs/{{side}}-{id}-decr", $"armslegs/{{side}}-{id}-incr", Mirrored: true);
}
