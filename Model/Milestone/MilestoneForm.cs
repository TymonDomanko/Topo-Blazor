using System.Text.Json;

namespace Topo.Model.Milestone
{
    public class MilestoneChoice
    {
        public string Id { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
    }

    // One question of the milestone review form
    public class MilestoneFormQuestion
    {
        public string Id { get; set; } = string.Empty;
        public string Label { get; set; } = string.Empty;
        // A question answered by picking from options (the SPICES objectives) rather than writing
        public bool IsChoice { get; set; }
        public List<MilestoneChoice> Choices { get; set; } = new List<MilestoneChoice>();
    }

    // The milestone review form as Terrain words it, one version of it
    public class MilestoneForm
    {
        public List<MilestoneFormQuestion> Questions { get; set; } = new List<MilestoneFormQuestion>();
    }

    public static class MilestoneFormParser
    {
        // The questions a Scout answers, in the order they are asked. The file uploader, the link and any plain text are not questions, so
        // they are left out. A template that can't be read gives no questions.
        public static MilestoneForm Parse(string json)
        {
            var form = new MilestoneForm();
            try
            {
                using var document = JsonDocument.Parse(json);
                if (document.RootElement.ValueKind == JsonValueKind.Object && document.RootElement.TryGetProperty("document", out var body))
                {
                    Walk(body, form);
                }
            }
            catch (JsonException)
            {
            }
            return form;
        }

        private static void Walk(JsonElement element, MilestoneForm form)
        {
            if (element.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in element.EnumerateArray())
                    Walk(item, form);
            }
            else if (element.ValueKind == JsonValueKind.Object)
            {
                var id = Text(element, "id");
                var type = Text(element, "type");
                if (id != "" && type != "" && element.TryGetProperty("label", out _))
                {
                    if (type == "text_input")
                    {
                        form.Questions.Add(new MilestoneFormQuestion { Id = id, Label = Label(element) });
                    }
                    else if (type.EndsWith("selector", StringComparison.Ordinal) || type == "radio")
                    {
                        form.Questions.Add(new MilestoneFormQuestion { Id = id, Label = Label(element), IsChoice = true, Choices = Choices(element) });
                    }
                    return;
                }
                foreach (var property in element.EnumerateObject())
                    Walk(property.Value, form);
            }
        }

        // The options, listed directly or under tabs
        private static List<MilestoneChoice> Choices(JsonElement question)
        {
            var choices = new List<MilestoneChoice>();
            void Add(JsonElement selections)
            {
                if (selections.ValueKind != JsonValueKind.Array)
                {
                    return;
                }
                foreach (var selection in selections.EnumerateArray())
                    if (Text(selection, "id") != "")
                    {
                        choices.Add(new MilestoneChoice { Id = Text(selection, "id"), Title = Text(selection, "title") });
                    }
            }
            if (question.TryGetProperty("selections", out var direct))
            {
                Add(direct);
            }
            if (question.TryGetProperty("tabs", out var tabs) && tabs.ValueKind == JsonValueKind.Array)
            {
                foreach (var tab in tabs.EnumerateArray())
                    if (tab.TryGetProperty("selections", out var inTab))
                    {
                        Add(inTab);
                    }
            }
            return choices;
        }

        private static string Label(JsonElement element) => Text(element, "label").TrimEnd('*').Trim();

        private static string Text(JsonElement element, string name) =>
            element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : "";
    }
}
