using System;
using System.Collections.Generic;
using System.IO;
using Messages;
using Newtonsoft.Json.Linq;
using Yaml;

namespace Durango.Online;

/// <summary>
/// แท็กที่โต๊ะคราฟต์แต่ละแบบมอบให้ — ตัวปลดล็อกการคราฟต์ 587 จาก 720 สูตร
///
/// ═══ ทำไมต้องมี ═══
/// สูตรส่วนใหญ่บอกว่า "ต้องมีโต๊ะที่มีแท็ก X ระดับ ≥ N" (recipes.json → workbench_tags)
/// ฝั่งเกมเช็คเองที่ <c>client/Crafting/Recipe.cs:59-68</c> ด้วย
/// <c>workbench.GetTag(id).Level &gt;= level</c> โดยแท็กมาจาก <c>AppearArtifact.Tags</c>
/// ที่เซิร์ฟส่งมาอย่างเดียว (<c>client/Artifact.cs:545 SetTagList</c>)
///
/// **ถ้าไม่ส่ง Tags มาเลย ผลคือปุ่มคราฟต์กดไม่ขึ้นโดยไม่มีข้อความบอกอะไร** — เกมจะบอกแค่
/// "ต้องมีโต๊ะทำงานอยู่ใกล้ ๆ" ทั้งที่ผู้เล่นยืนอยู่หน้าโต๊ะพอดี
///
/// ═══ ตารางนี้เราสร้างเอง ═══
/// ข้อมูลเกมที่มีไม่ได้บอกว่าโต๊ะตัวไหนให้แท็กอะไร (ตรวจครบทุกไฟล์แล้ว — รายละเอียดและ
/// หลักฐานที่ใช้อนุมานอยู่ใน <c>tools/derive-workbench-tags.py</c>)
/// ⇒ สร้างเป็นไฟล์ <c>data/assets/derived/workbench_tags.json</c> ที่แก้ได้โดยไม่ต้องแก้โค้ด
/// และมีสนาม <c>_ที่มา</c> กำกับความมั่นใจของทุกแท็กไว้ในไฟล์นั้นเลย
///
/// ไม่มีไฟล์ ⇒ ไม่พัง แค่คราฟต์ที่ต้องใช้โต๊ะไม่ได้ (เตือนออกล็อกครั้งเดียว)
/// </summary>
public static class WorkbenchTags
{
    private static Dictionary<string, Tag[]> _byPrototype;

    /// <summary>โฟลเดอร์ assets — ตั้งจาก Program ตอนบูต (ทางเดียวกับ RegionCatalog.Load)</summary>
    public static string AssetsDir { get; set; }

    /// <summary>
    /// แท็กของสิ่งปลูกสร้างชนิดนี้ — คืน null ถ้าไม่ใช่โต๊ะคราฟต์
    /// (คืน null ไม่ใช่อาเรย์ว่าง เพราะฝั่งเกมนับ <c>Tags.Length</c> แล้ววนอ่าน ไม่ได้แยกสองกรณี)
    /// </summary>
    public static Tag[] Of(int entityType)
    {
        EnsureLoaded();
        MergedBlueprint blueprint = BlueprintStore.GetBlueprint(entityType);
        if (blueprint?.Id == null) return null;
        return _byPrototype.TryGetValue(blueprint.Id, out Tag[] tags) ? tags : null;
    }

    /// <summary>
    /// เติม/ผสานแท็กโต๊ะคราฟต์ลงใน AppearArtifact — คืน true เมื่อมีการเปลี่ยนแปลง
    ///
    /// FACILDIGITAL+: merge tags derivadas de bancada.
    /// ของเดิมหยุดทันทีเมื่อ artifact มีแท็กอะไรก็ได้อยู่แล้ว ทำให้โต๊ะบางหลังขาด cook/workbench/kitchen.
    /// ตอนนี้เก็บแท็กเดิมไว้ และเติมเฉพาะแท็กที่ขาดหรือมีระดับต่ำกว่าค่า derived.
    /// </summary>
    public static bool Apply(ref AppearArtifact artifact)
    {
        Tag[] derived = Of(artifact.EntityType);
        if (derived == null || derived.Length == 0) return false;

        var merged = new Dictionary<string, Tag>(StringComparer.Ordinal);
        if (artifact.Tags._Tags != null)
        {
            foreach (Tag tag in artifact.Tags._Tags)
            {
                if (string.IsNullOrEmpty(tag.Id)) continue;
                if (!merged.TryGetValue(tag.Id, out Tag previous) || tag.Level > previous.Level)
                    merged[tag.Id] = tag;
            }
        }

        bool changed = false;
        foreach (Tag tag in derived)
        {
            if (string.IsNullOrEmpty(tag.Id)) continue;
            if (!merged.TryGetValue(tag.Id, out Tag current) || current.Level < tag.Level)
            {
                merged[tag.Id] = tag;
                changed = true;
            }
        }

        if (!string.Equals(artifact.Tags.EntityId, artifact.EntityId, StringComparison.Ordinal))
            changed = true;
        if (!changed) return false;

        artifact.Tags = new Tags
        {
            EntityId = artifact.EntityId,
            _Tags = new List<Tag>(merged.Values).ToArray()
        };
        return true;
    }

    private static void EnsureLoaded()
    {
        if (_byPrototype != null) return;
        _byPrototype = new Dictionary<string, Tag[]>(StringComparer.Ordinal);

        string path = Path.Combine(AssetsDir ?? "", "derived", "workbench_tags.json");
        if (!File.Exists(path))
        {
            Console.WriteLine($"[โต๊ะคราฟต์] ⚠️ ไม่พบ {path} — สูตรที่ต้องใช้โต๊ะจะคราฟต์ไม่ได้ " +
                              "(สร้างใหม่ด้วย: python tools/derive-workbench-tags.py)");
            return;
        }

        try
        {
            var root = JObject.Parse(File.ReadAllText(path));
            var benches = root["Bancada"] as JObject ?? root["โต๊ะ"] as JObject;
            if (benches == null)
            {
                Console.WriteLine($"[โต๊ะคราฟต์] ⚠️ {path} ไม่มีคีย์ \"โต๊ะ\"");
                return;
            }

            foreach (JProperty bench in benches.Properties())
            {
                if (bench.Value is not JObject tagLevels) continue;
                var list = new List<Tag>();
                foreach (JProperty pair in tagLevels.Properties())
                {
                    if ((int?)pair.Value is { } level) list.Add(new Tag { Id = pair.Name, Level = level });
                }
                if (list.Count > 0) _byPrototype[bench.Name] = list.ToArray();
            }

            Console.WriteLine($"[โต๊ะคราฟต์] โหลดแท็กของโต๊ะ {_byPrototype.Count} แบบ");
        }
        catch (Exception e)
        {
            Console.WriteLine($"[โต๊ะคราฟต์] อ่าน {path} ไม่ได้: {e.Message}");
        }
    }
}
