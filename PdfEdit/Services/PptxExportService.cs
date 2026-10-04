using System.IO;
using System.IO.Compression;
using System.Security;
using System.Text;

namespace PdfEdit.Services;

/// <summary>One slide: the page as a PNG, its size in points, and its text (kept as the picture's alt text).</summary>
public sealed record PptxSlide(byte[] Png, double WidthPt, double HeightPt, string Text);

/// <summary>
/// Export to PowerPoint: one slide per page, each page as a full-slide picture, in a .pptx that
/// PowerPoint, Keynote, Google Slides and LibreOffice open. The slide size follows the first page.
/// </summary>
public static class PptxExportService
{
    private const string NsA = "http://schemas.openxmlformats.org/drawingml/2006/main";
    private const string NsR = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private const string NsP = "http://schemas.openxmlformats.org/presentationml/2006/main";
    private const string RelNs = "http://schemas.openxmlformats.org/package/2006/relationships";
    private const string RelBase = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/";
    private const long EmuPerPt = 12700;

    public static void Export(string path, IReadOnlyList<PptxSlide> slides)
    {
        if (slides.Count == 0) throw new InvalidOperationException("There are no pages to export.");
        // PowerPoint allows slides from 1 to 56 inches on a side.
        long cx = Math.Clamp((long)(slides[0].WidthPt * EmuPerPt), 914400, 51206400);
        long cy = Math.Clamp((long)(slides[0].HeightPt * EmuPerPt), 914400, 51206400);

        if (File.Exists(path)) File.Delete(path);
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        void Text(string name, string content)
        {
            var e = zip.CreateEntry(name, CompressionLevel.Optimal);
            using var w = new StreamWriter(e.Open(), new UTF8Encoding(false));
            w.Write("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\n");
            w.Write(content);
        }
        void Bytes(string name, byte[] data)
        {
            var e = zip.CreateEntry(name, CompressionLevel.NoCompression);
            using var s = e.Open();
            s.Write(data);
        }

        var ct = new StringBuilder($"<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">" +
            "<Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/>" +
            "<Default Extension=\"xml\" ContentType=\"application/xml\"/>" +
            "<Default Extension=\"png\" ContentType=\"image/png\"/>" +
            "<Override PartName=\"/ppt/presentation.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.presentationml.presentation.main+xml\"/>" +
            "<Override PartName=\"/ppt/slideMasters/slideMaster1.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.presentationml.slideMaster+xml\"/>" +
            "<Override PartName=\"/ppt/slideLayouts/slideLayout1.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.presentationml.slideLayout+xml\"/>" +
            "<Override PartName=\"/ppt/theme/theme1.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.theme+xml\"/>" +
            "<Override PartName=\"/ppt/presProps.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.presentationml.presProps+xml\"/>" +
            "<Override PartName=\"/ppt/tableStyles.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.presentationml.tableStyles+xml\"/>");
        for (int i = 1; i <= slides.Count; i++)
            ct.Append($"<Override PartName=\"/ppt/slides/slide{i}.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.presentationml.slide+xml\"/>");
        ct.Append("</Types>");
        Text("[Content_Types].xml", ct.ToString());

        Text("_rels/.rels", $"<Relationships xmlns=\"{RelNs}\"><Relationship Id=\"rId1\" Type=\"{RelBase}officeDocument\" Target=\"ppt/presentation.xml\"/></Relationships>");

        // Presentation
        var pres = new StringBuilder($"<p:presentation xmlns:a=\"{NsA}\" xmlns:r=\"{NsR}\" xmlns:p=\"{NsP}\" saveSubsetFonts=\"1\">" +
            "<p:sldMasterIdLst><p:sldMasterId id=\"2147483648\" r:id=\"rId1\"/></p:sldMasterIdLst><p:sldIdLst>");
        for (int i = 1; i <= slides.Count; i++) pres.Append($"<p:sldId id=\"{255 + i}\" r:id=\"rId{i + 2}\"/>");
        pres.Append($"</p:sldIdLst><p:sldSz cx=\"{cx}\" cy=\"{cy}\"/><p:notesSz cx=\"6858000\" cy=\"9144000\"/></p:presentation>");
        Text("ppt/presentation.xml", pres.ToString());

        var presRels = new StringBuilder($"<Relationships xmlns=\"{RelNs}\">" +
            $"<Relationship Id=\"rId1\" Type=\"{RelBase}slideMaster\" Target=\"slideMasters/slideMaster1.xml\"/>" +
            $"<Relationship Id=\"rId2\" Type=\"{RelBase}theme\" Target=\"theme/theme1.xml\"/>");
        for (int i = 1; i <= slides.Count; i++)
            presRels.Append($"<Relationship Id=\"rId{i + 2}\" Type=\"{RelBase}slide\" Target=\"slides/slide{i}.xml\"/>");
        presRels.Append($"<Relationship Id=\"rId{slides.Count + 3}\" Type=\"{RelBase}presProps\" Target=\"presProps.xml\"/>" +
                        $"<Relationship Id=\"rId{slides.Count + 4}\" Type=\"{RelBase}tableStyles\" Target=\"tableStyles.xml\"/></Relationships>");
        Text("ppt/_rels/presentation.xml.rels", presRels.ToString());

        Text("ppt/presProps.xml", $"<p:presentationPr xmlns:a=\"{NsA}\" xmlns:r=\"{NsR}\" xmlns:p=\"{NsP}\"/>");
        Text("ppt/tableStyles.xml", $"<a:tblStyleLst xmlns:a=\"{NsA}\" def=\"{{5C22544A-7EE6-4342-B048-85BDC9FD1C3A}}\"/>");

        // Master and the one blank layout
        Text("ppt/slideMasters/slideMaster1.xml", $"<p:sldMaster xmlns:a=\"{NsA}\" xmlns:r=\"{NsR}\" xmlns:p=\"{NsP}\">" +
            $"<p:cSld><p:bg><p:bgRef idx=\"1001\"><a:schemeClr val=\"bg1\"/></p:bgRef></p:bg>{EmptyTree}</p:cSld>" +
            "<p:clrMap bg1=\"lt1\" tx1=\"dk1\" bg2=\"lt2\" tx2=\"dk2\" accent1=\"accent1\" accent2=\"accent2\" accent3=\"accent3\" accent4=\"accent4\" accent5=\"accent5\" accent6=\"accent6\" hlink=\"hlink\" folHlink=\"folHlink\"/>" +
            "<p:sldLayoutIdLst><p:sldLayoutId id=\"2147483649\" r:id=\"rId1\"/></p:sldLayoutIdLst></p:sldMaster>");
        Text("ppt/slideMasters/_rels/slideMaster1.xml.rels", $"<Relationships xmlns=\"{RelNs}\">" +
            $"<Relationship Id=\"rId1\" Type=\"{RelBase}slideLayout\" Target=\"../slideLayouts/slideLayout1.xml\"/>" +
            $"<Relationship Id=\"rId2\" Type=\"{RelBase}theme\" Target=\"../theme/theme1.xml\"/></Relationships>");
        Text("ppt/slideLayouts/slideLayout1.xml", $"<p:sldLayout xmlns:a=\"{NsA}\" xmlns:r=\"{NsR}\" xmlns:p=\"{NsP}\" type=\"blank\" preserve=\"1\">" +
            $"<p:cSld name=\"Blank\">{EmptyTree}</p:cSld><p:clrMapOvr><a:masterClrMapping/></p:clrMapOvr></p:sldLayout>");
        Text("ppt/slideLayouts/_rels/slideLayout1.xml.rels", $"<Relationships xmlns=\"{RelNs}\">" +
            $"<Relationship Id=\"rId1\" Type=\"{RelBase}slideMaster\" Target=\"../slideMasters/slideMaster1.xml\"/></Relationships>");
        Text("ppt/theme/theme1.xml", Theme);

        // Slides
        for (int i = 1; i <= slides.Count; i++)
        {
            var s = slides[i - 1];
            // Fit the page inside the slide, centred (pages can differ in size).
            double scale = Math.Min(cx / (s.WidthPt * EmuPerPt), cy / (s.HeightPt * EmuPerPt));
            long w = (long)(s.WidthPt * EmuPerPt * scale), h = (long)(s.HeightPt * EmuPerPt * scale);
            long x = (cx - w) / 2, y = (cy - h) / 2;
            string alt = Esc(s.Text.Length > 1500 ? s.Text[..1500] + "…" : s.Text);
            Bytes($"ppt/media/image{i}.png", s.Png);
            Text($"ppt/slides/slide{i}.xml", $"<p:sld xmlns:a=\"{NsA}\" xmlns:r=\"{NsR}\" xmlns:p=\"{NsP}\"><p:cSld><p:spTree>" +
                "<p:nvGrpSpPr><p:cNvPr id=\"1\" name=\"\"/><p:cNvGrpSpPr/><p:nvPr/></p:nvGrpSpPr>" +
                "<p:grpSpPr><a:xfrm><a:off x=\"0\" y=\"0\"/><a:ext cx=\"0\" cy=\"0\"/><a:chOff x=\"0\" y=\"0\"/><a:chExt cx=\"0\" cy=\"0\"/></a:xfrm></p:grpSpPr>" +
                $"<p:pic><p:nvPicPr><p:cNvPr id=\"2\" name=\"Page {i}\" descr=\"{alt}\"/><p:cNvPicPr><a:picLocks noChangeAspect=\"1\"/></p:cNvPicPr><p:nvPr/></p:nvPicPr>" +
                "<p:blipFill><a:blip r:embed=\"rId2\"/><a:stretch><a:fillRect/></a:stretch></p:blipFill>" +
                $"<p:spPr><a:xfrm><a:off x=\"{x}\" y=\"{y}\"/><a:ext cx=\"{w}\" cy=\"{h}\"/></a:xfrm><a:prstGeom prst=\"rect\"><a:avLst/></a:prstGeom></p:spPr></p:pic>" +
                "</p:spTree></p:cSld><p:clrMapOvr><a:masterClrMapping/></p:clrMapOvr></p:sld>");
            Text($"ppt/slides/_rels/slide{i}.xml.rels", $"<Relationships xmlns=\"{RelNs}\">" +
                $"<Relationship Id=\"rId1\" Type=\"{RelBase}slideLayout\" Target=\"../slideLayouts/slideLayout1.xml\"/>" +
                $"<Relationship Id=\"rId2\" Type=\"{RelBase}image\" Target=\"../media/image{i}.png\"/></Relationships>");
        }
    }

    private const string EmptyTree =
        "<p:spTree><p:nvGrpSpPr><p:cNvPr id=\"1\" name=\"\"/><p:cNvGrpSpPr/><p:nvPr/></p:nvGrpSpPr>" +
        "<p:grpSpPr><a:xfrm><a:off x=\"0\" y=\"0\"/><a:ext cx=\"0\" cy=\"0\"/><a:chOff x=\"0\" y=\"0\"/><a:chExt cx=\"0\" cy=\"0\"/></a:xfrm></p:grpSpPr></p:spTree>";

    private static string Esc(string s) =>
        SecurityElement.Escape(new string(s.Where(c => c == '\n' || c == '\t' || c >= 0x20).ToArray()).Replace("\n", " ")) ?? "";

    private const string Theme =
        "<a:theme xmlns:a=\"http://schemas.openxmlformats.org/drawingml/2006/main\" name=\"Office Theme\"><a:themeElements>" +
        "<a:clrScheme name=\"Office\">" +
        "<a:dk1><a:sysClr val=\"windowText\" lastClr=\"000000\"/></a:dk1><a:lt1><a:sysClr val=\"window\" lastClr=\"FFFFFF\"/></a:lt1>" +
        "<a:dk2><a:srgbClr val=\"44546A\"/></a:dk2><a:lt2><a:srgbClr val=\"E7E6E6\"/></a:lt2>" +
        "<a:accent1><a:srgbClr val=\"4472C4\"/></a:accent1><a:accent2><a:srgbClr val=\"ED7D31\"/></a:accent2>" +
        "<a:accent3><a:srgbClr val=\"A5A5A5\"/></a:accent3><a:accent4><a:srgbClr val=\"FFC000\"/></a:accent4>" +
        "<a:accent5><a:srgbClr val=\"5B9BD5\"/></a:accent5><a:accent6><a:srgbClr val=\"70AD47\"/></a:accent6>" +
        "<a:hlink><a:srgbClr val=\"0563C1\"/></a:hlink><a:folHlink><a:srgbClr val=\"954F72\"/></a:folHlink></a:clrScheme>" +
        "<a:fontScheme name=\"Office\">" +
        "<a:majorFont><a:latin typeface=\"Calibri Light\"/><a:ea typeface=\"\"/><a:cs typeface=\"\"/></a:majorFont>" +
        "<a:minorFont><a:latin typeface=\"Calibri\"/><a:ea typeface=\"\"/><a:cs typeface=\"\"/></a:minorFont></a:fontScheme>" +
        "<a:fmtScheme name=\"Office\">" +
        "<a:fillStyleLst><a:solidFill><a:schemeClr val=\"phClr\"/></a:solidFill><a:solidFill><a:schemeClr val=\"phClr\"/></a:solidFill><a:solidFill><a:schemeClr val=\"phClr\"/></a:solidFill></a:fillStyleLst>" +
        "<a:lnStyleLst><a:ln w=\"6350\"><a:solidFill><a:schemeClr val=\"phClr\"/></a:solidFill></a:ln><a:ln w=\"12700\"><a:solidFill><a:schemeClr val=\"phClr\"/></a:solidFill></a:ln><a:ln w=\"19050\"><a:solidFill><a:schemeClr val=\"phClr\"/></a:solidFill></a:ln></a:lnStyleLst>" +
        "<a:effectStyleLst><a:effectStyle><a:effectLst/></a:effectStyle><a:effectStyle><a:effectLst/></a:effectStyle><a:effectStyle><a:effectLst/></a:effectStyle></a:effectStyleLst>" +
        "<a:bgFillStyleLst><a:solidFill><a:schemeClr val=\"phClr\"/></a:solidFill><a:solidFill><a:schemeClr val=\"phClr\"/></a:solidFill><a:solidFill><a:schemeClr val=\"phClr\"/></a:solidFill></a:bgFillStyleLst>" +
        "</a:fmtScheme></a:themeElements><a:objectDefaults/><a:extraClrSchemeLst/></a:theme>";
}
