using System.IO.Compression;
using System.Security;
using System.Text;

namespace Topo.Model.ProgramBuilder
{
    // Writes the program as a Word document (.docx): a landscape table of the section nights and activities, then the locations and the
    // contact details. A .docx is a zip of a few xml files, so this needs no library and can run in the browser.
    public static class ProgramDocx
    {
        // A4 landscape, in twentieths of a point, with narrow margins
        private const int PageWidth = 16838;
        private const int PageHeight = 11906;
        private const int Margin = 720;
        private const int UsableWidth = PageWidth - 2 * Margin;

        public static byte[] Create(ProgramDocument program)
        {
            using var stream = new MemoryStream();
            using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
            {
                Add(zip, "[Content_Types].xml", ContentTypes);
                Add(zip, "_rels/.rels", Relationships);
                Add(zip, "word/_rels/document.xml.rels", DocumentRelationships);
                Add(zip, "word/styles.xml", Styles);
                Add(zip, "word/document.xml", Document(program));
            }
            return stream.ToArray();
        }

        private static void Add(ZipArchive zip, string name, string content)
        {
            var entry = zip.CreateEntry(name, CompressionLevel.Optimal);
            using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
            writer.Write(content);
        }

        public static string Document(ProgramDocument program)
        {
            var xml = new StringBuilder();
            xml.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>");
            xml.Append("<w:document xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\"><w:body>");
            xml.Append(Paragraph(program.Title, bold: true, size: 32, centred: true, after: 160));
            xml.Append(Calendar(program));
            if (program.Locations.Count > 0)
            {
                xml.Append(Paragraph("Locations", bold: true, size: 28, centred: true, before: 360, after: 120));
                xml.Append(Simple(new[] { "Location", "Address" }, new[] { 2800, 4800 }, program.Locations.Select(l => new[] { l.Name, l.Address }), centredTable: true));
            }
            if (program.Contacts.Count > 0)
            {
                xml.Append(Paragraph("Contact Details", bold: true, size: 28, centred: true, before: 360, after: 120));
                xml.Append(Simple(new[] { "Leader", "Position", "Email" }, new[] { 3000, 2600, 6000 }, program.Contacts.Select(c => new[] { c.Name, c.Position, c.Email }), centredTable: false));
            }
            xml.Append($"<w:sectPr><w:pgSz w:w=\"{PageWidth}\" w:h=\"{PageHeight}\" w:orient=\"landscape\"/><w:pgMar w:top=\"{Margin}\" w:right=\"{Margin}\" w:bottom=\"{Margin}\" w:left=\"{Margin}\" w:header=\"360\" w:footer=\"360\" w:gutter=\"0\"/></w:sectPr>");
            xml.Append("</w:body></w:document>");
            return xml.ToString();
        }

        // The columns shown after the date and activity: their heading, their width in twentieths of a point, and what each row puts in it
        private static readonly Dictionary<string, (string Heading, int Width, Func<ProgramDocRow, string> Value)> Optional = new Dictionary<string, (string, int, Func<ProgramDocRow, string>)>
        {
            ["game"] = ("Game", 2200, r => r.Game),
            ["location"] = ("Location", 2700, r => r.Location),
            ["challenge"] = ("Challenge Area", 1700, r => r.ChallengeArea),
            ["duty"] = ("Duty Patrol", 1300, r => r.DutyPatrol),
            ["leader"] = ("LIC", 1700, r => r.Leader)
        };

        // The widths of the month, the date and the activity and then the columns that are shown, widened to fill the page
        private static List<int> Widths(ProgramDocument program)
        {
            var widths = new List<int> { 560, 1000, 3500 };
            widths.AddRange(program.Columns.Select(c => Optional[c].Width));
            var scale = (double)UsableWidth / widths.Sum();
            return widths.Select(w => (int)(w * scale)).ToList();
        }

        private static string Calendar(ProgramDocument program)
        {
            var columns = Widths(program);

            var xml = new StringBuilder();
            xml.Append("<w:tbl><w:tblPr><w:tblW w:w=\"" + columns.Sum() + "\" w:type=\"dxa\"/><w:jc w:val=\"center\"/>" + Borders() + "<w:tblLayout w:type=\"fixed\"/><w:tblCellMar><w:left w:w=\"80\" w:type=\"dxa\"/><w:right w:w=\"80\" w:type=\"dxa\"/></w:tblCellMar></w:tblPr>");
            xml.Append("<w:tblGrid>" + string.Concat(columns.Select(c => $"<w:gridCol w:w=\"{c}\"/>")) + "</w:tblGrid>");

            // the heading row, with "Date" over the month and the day
            xml.Append("<w:tr><w:trPr><w:cantSplit/><w:tblHeader/></w:trPr>");
            xml.Append(Cell("Date", columns[0] + columns[1], bold: true, fill: "D9D9D9", span: 2));
            xml.Append(Cell("Activity", columns[2], bold: true, fill: "D9D9D9"));
            for (var i = 0; i < program.Columns.Count; i++)
                xml.Append(Cell(Optional[program.Columns[i]].Heading, columns[3 + i], bold: true, fill: "D9D9D9"));
            xml.Append("</w:tr>");

            var band = false;
            var previousVertical = false;
            foreach (var row in program.Rows)
            {
                xml.Append("<w:tr><w:trPr><w:cantSplit/></w:trPr>");
                if (row.Month != "")
                    previousVertical = row.MonthSpan >= 3;
                if (row.IsBreak)
                {
                    xml.Append(Cell(row.BreakLabel, columns.Sum(), bold: true, fill: "A6A6A6", span: columns.Count, centred: true));
                    xml.Append("</w:tr>");
                    band = false;
                    continue;
                }

                var fill = band ? "F2F2F2" : null;
                band = !band;
                // a month of one or two rows is too short to read turned sideways, so it is written across, as three letters
                if (row.Month != "")
                    xml.Append(row.MonthSpan >= 3
                        ? Cell(row.Month, columns[0], fill: "FFFFFF", vertical: true, merge: "restart", centred: true)
                        : Cell(row.Month.Substring(0, Math.Min(3, row.Month.Length)), columns[0], fill: "FFFFFF", merge: row.MonthSpan > 1 ? "restart" : null, centred: true));
                else
                    xml.Append(Cell("", columns[0], fill: "FFFFFF", vertical: previousVertical, merge: "continue", centred: true));
                xml.Append(Cell(row.DateText, columns[1], fill: fill));
                xml.Append(Cell(row.Activity, columns[2], fill: fill));
                for (var i = 0; i < program.Columns.Count; i++)
                    xml.Append(Cell(Optional[program.Columns[i]].Value(row), columns[3 + i], fill: fill));
                xml.Append("</w:tr>");
            }
            xml.Append("</w:tbl>");
            return xml.ToString();
        }

        // A table of plain rows under a shaded heading row
        private static string Simple(string[] headings, int[] widths, IEnumerable<string[]> rows, bool centredTable)
        {
            var xml = new StringBuilder();
            xml.Append("<w:tbl><w:tblPr><w:tblW w:w=\"" + widths.Sum() + "\" w:type=\"dxa\"/>" + (centredTable ? "<w:jc w:val=\"center\"/>" : "") + Borders() + "<w:tblLayout w:type=\"fixed\"/><w:tblCellMar><w:left w:w=\"80\" w:type=\"dxa\"/><w:right w:w=\"80\" w:type=\"dxa\"/></w:tblCellMar></w:tblPr>");
            xml.Append("<w:tblGrid>" + string.Concat(widths.Select(c => $"<w:gridCol w:w=\"{c}\"/>")) + "</w:tblGrid>");
            xml.Append("<w:tr><w:trPr><w:cantSplit/><w:tblHeader/></w:trPr>" + string.Concat(headings.Select((h, i) => Cell(h, widths[i], bold: true, fill: "D9D9D9"))) + "</w:tr>");
            var band = false;
            foreach (var row in rows)
            {
                xml.Append("<w:tr><w:trPr><w:cantSplit/></w:trPr>" + string.Concat(row.Select((value, i) => Cell(value, widths[i], fill: band ? "F2F2F2" : null))) + "</w:tr>");
                band = !band;
            }
            xml.Append("</w:tbl>");
            return xml.ToString();
        }

        private static string Borders() =>
            "<w:tblBorders>" + string.Concat(new[] { "top", "left", "bottom", "right", "insideH", "insideV" }.Select(side => $"<w:{side} w:val=\"single\" w:sz=\"4\" w:space=\"0\" w:color=\"7F7F7F\"/>")) + "</w:tblBorders>";

        private static string Cell(string text, int width, bool bold = false, string? fill = null, int span = 1, bool vertical = false, string? merge = null, bool centred = false)
        {
            var properties = $"<w:tcW w:w=\"{width}\" w:type=\"dxa\"/>"
                + (span > 1 ? $"<w:gridSpan w:val=\"{span}\"/>" : "")
                + (merge != null ? (merge == "restart" ? "<w:vMerge w:val=\"restart\"/>" : "<w:vMerge/>") : "")
                + (fill != null ? $"<w:shd w:val=\"clear\" w:color=\"auto\" w:fill=\"{fill}\"/>" : "")
                + (vertical ? "<w:textDirection w:val=\"btLr\"/>" : "")
                + "<w:vAlign w:val=\"center\"/>";
            return $"<w:tc><w:tcPr>{properties}</w:tcPr>{Paragraph(text, bold, 20, centred, 0, 0)}</w:tc>";
        }

        private static string Paragraph(string text, bool bold = false, int size = 20, bool centred = false, int before = 0, int after = 0)
        {
            var run = text == "" ? "" : $"<w:r><w:rPr>{(bold ? "<w:b/>" : "")}<w:sz w:val=\"{size}\"/><w:szCs w:val=\"{size}\"/></w:rPr><w:t xml:space=\"preserve\">{SecurityElement.Escape(text)}</w:t></w:r>";
            return $"<w:p><w:pPr><w:spacing w:before=\"{before}\" w:after=\"{after}\"/>{(centred ? "<w:jc w:val=\"center\"/>" : "")}</w:pPr>{run}</w:p>";
        }

        private const string ContentTypes =
            "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">"
            + "<Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/><Default Extension=\"xml\" ContentType=\"application/xml\"/>"
            + "<Override PartName=\"/word/document.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml\"/>"
            + "<Override PartName=\"/word/styles.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.styles+xml\"/></Types>";

        private const string Relationships =
            "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">"
            + "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"word/document.xml\"/></Relationships>";

        private const string DocumentRelationships =
            "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">"
            + "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles\" Target=\"styles.xml\"/></Relationships>";

        private const string Styles =
            "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><w:styles xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\">"
            + "<w:docDefaults><w:rPrDefault><w:rPr><w:rFonts w:ascii=\"Calibri\" w:hAnsi=\"Calibri\" w:cs=\"Calibri\"/><w:sz w:val=\"20\"/><w:szCs w:val=\"20\"/></w:rPr></w:rPrDefault>"
            + "<w:pPrDefault><w:pPr><w:spacing w:after=\"0\" w:line=\"240\" w:lineRule=\"auto\"/></w:pPr></w:pPrDefault></w:docDefaults>"
            + "<w:style w:type=\"paragraph\" w:default=\"1\" w:styleId=\"Normal\"><w:name w:val=\"Normal\"/></w:style></w:styles>";
    }
}
