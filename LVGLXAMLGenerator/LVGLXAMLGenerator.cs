using System.Globalization;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

[Generator]
public sealed class LVGLXAMLGenerator : IIncrementalGenerator
{
    private static readonly DiagnosticDescriptor InvalidXaml = new(
        "LVGLXAML001", "Invalid LVGL XAML", "{0}", "LVGL XAML", DiagnosticSeverity.Error, true);

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var pages = context.AdditionalTextsProvider
            .Where(static file => file.Path.EndsWith(".xaml", StringComparison.OrdinalIgnoreCase))
            .Select(static (file, cancellationToken) =>
                (Path: file.Path, Text: file.GetText(cancellationToken)));

        context.RegisterSourceOutput(pages, static (production, page) =>
        {
            if (page.Text is null)
            {
                production.ReportDiagnostic(Diagnostic.Create(InvalidXaml, Location.None,
                    $"Cannot read '{page.Path}'."));
                return;
            }

            try
            {
                string generated = new PageGenerator(page.Text.ToString()).Generate();
                production.AddSource(Path.GetFileName(page.Path) + ".g.cs", SourceText.From(generated, Encoding.UTF8));
            }
            catch (Exception exception) when (exception is XmlException or XamlGenerationException)
            {
                int line = exception is XmlException xml ? xml.LineNumber : ((XamlGenerationException)exception).LineNumber;
                int column = exception is XmlException xmlError ? xmlError.LinePosition : ((XamlGenerationException)exception).ColumnNumber;
                int lineIndex = Math.Min(Math.Max(line - 1, 0), page.Text.Lines.Count - 1);
                int columnIndex = Math.Min(Math.Max(column - 1, 0), page.Text.Lines[lineIndex].Span.Length);
                var position = new LinePosition(lineIndex, columnIndex);
                Location location = Location.Create(page.Path,
                    new TextSpan(page.Text.Lines[lineIndex].Start + columnIndex, 0),
                    new LinePositionSpan(position, position));
                production.ReportDiagnostic(Diagnostic.Create(InvalidXaml, location, exception.Message));
            }
        });
    }
}

internal sealed class PageGenerator
{
    private static readonly XNamespace SchemaInstance = "http://www.w3.org/2001/XMLSchema-instance";
    private static readonly HashSet<string> Alignments = new(StringComparer.Ordinal)
    {
        "Default", "TopLeft", "TopMid", "TopRight", "BottomLeft", "BottomMid", "BottomRight",
        "LeftMid", "RightMid", "Center", "OutTopLeft", "OutTopMid", "OutTopRight",
        "OutBottomLeft", "OutBottomMid", "OutBottomRight", "OutLeftTop", "OutLeftMid",
        "OutLeftBottom", "OutRightTop", "OutRightMid", "OutRightBottom"
    };
    private static readonly Dictionary<string, string> Controls = new(StringComparer.Ordinal)
    {
        ["Object"] = "LVObject", ["Label"] = "LVLabel", ["Slider"] = "LVSlider",
        ["Bar"] = "LVBar", ["Switch"] = "LVSwitch", ["Checkbox"] = "LVCheckbox",
        ["Button"] = "LVButton", ["Arc"] = "LVArc", ["Dropdown"] = "LVDropdown",
        ["Roller"] = "LVRoller", ["TextArea"] = "LVTextArea", ["Table"] = "LVTable"
    };
    private static readonly HashSet<string> Attributes = new(StringComparer.Ordinal)
    {
        "Text", "Width", "Height", "Align", "RelativeTo", "RelativeAlign", "OffsetX", "OffsetY",
        "PadAll", "PadTop", "PadBottom", "ClearFlag", "AddFlag", "ScrollDirection", "UpdateLayout",
        "RangeMin", "RangeMax", "Value", "Checked", "On", "Filter", "EventData",
        "PadLeft", "PadRight", "X", "Y", "AddState", "ClearState", "ExtClickArea",
        "LongMode", "Recolor", "Mode", "StartValue", "LeftValue",
        "StartAngle", "EndAngle", "BackgroundStartAngle", "BackgroundEndAngle", "Rotation",
        "Options", "Selected", "Open", "VisibleRowCount", "PlaceholderText", "OneLine",
        "PasswordMode", "MaxLength", "CursorPosition", "RowCount", "ColumnCount"
    };

    private readonly XElement root;
    private readonly StringBuilder statements = new();
    private readonly StringBuilder fields = new();
    private readonly HashSet<string> names = new(StringComparer.Ordinal) { "screen", "navigationTarget" };
    private readonly Dictionary<string, string> namedTypes = new(StringComparer.Ordinal)
    {
        ["screen"] = "LVObject", ["navigationTarget"] = "LVObject"
    };
    private int anonymousIndex;

    public PageGenerator(string contents)
    {
        root = XDocument.Parse(contents, LoadOptions.SetLineInfo).Root
            ?? throw new XamlGenerationException("XAML must have a root element.", 0, 0);
    }

    public string Generate()
    {
        if (root.Name != "Screen")
            Fail(root, "Root element must be <Screen>.");
        foreach (XAttribute attribute in root.Attributes())
            if (attribute.IsNamespaceDeclaration == false && attribute.Name != "Class" && attribute.Name != "Method"
                && attribute.Name != SchemaInstance + "noNamespaceSchemaLocation")
                Fail(attribute, $"Unsupported screen attribute '{attribute.Name}'.");

        string className = Required(root, "Class");
        string methodName = Required(root, "Method");
        Identifier(root, className);
        Identifier(root, methodName);
        CheckContent(root);
        foreach (XElement child in root.Elements())
            Emit(child, "screen", "LVObject");

        string methodStatements = statements.ToString().TrimEnd('\r', '\n');
        string members = fields.ToString() + $$"""
                private static void {{methodName}}(LVObject screen, LVObject navigationTarget)
                {
            {{methodStatements}}
                }
            """;
        return $$"""
            using static LVGL;

            internal static unsafe partial class {{className}}
            {
            {{members}}
            }

            """;
    }

    private void Emit(XElement element, string parent, string parentType)
    {
        if (element.Name == "Style")
        {
            EmitStyle(element, parent, parentType);
            return;
        }
        if (element.Name.Namespace == XNamespace.None && element.Name.LocalName is "Cell" or "Column")
        {
            if (parentType != "LVTable") Fail(element, "Cell and Column are only supported inside Table.");
            EmitTableEntry(element, parent);
            return;
        }
        if (!Controls.TryGetValue(element.Name.LocalName, out string? type) || element.Name.Namespace != XNamespace.None)
            Fail(element, $"Unsupported control '{element.Name}'.");
        foreach (XAttribute attribute in element.Attributes())
            if (!attribute.IsNamespaceDeclaration && attribute.Name != "Name" && attribute.Name != "Field"
                && (attribute.Name.Namespace != XNamespace.None || !Attributes.Contains(attribute.Name.LocalName)))
                Fail(attribute, $"Unsupported attribute '{attribute.Name}'.");
        CheckContent(element);

        string? name = (string?)element.Attribute("Name");
        bool field = Boolean(element, "Field", false);
        if (field && name is null)
            Fail(element, "Field requires Name.");
        if (name is not null)
        {
            Identifier(element, name);
            if (!names.Add(name))
                Fail(element, $"Duplicate name '{name}'.");
            namedTypes.Add(name, type);
        }
        string variable = name ?? $"__xamlWidget{++anonymousIndex}";
        string parentObject = parentType == "LVObject" ? parent : $"{parent}.Object";
        string create = type == "LVObject" ? "CreateObject" : $"Create{element.Name.LocalName}";
        if (field)
        {
            fields.AppendLine($"    private static {type} {variable};");
            Line($"{variable} = {create}({parentObject});");
        }
        else
            Line($"{type} {variable} = {create}({parentObject});");

        string obj = type == "LVObject" ? variable : $"{variable}.Object";
        string? width = Get(element, "Width"), height = Get(element, "Height");
        if (width is not null && height is not null && width.EndsWith("%", StringComparison.Ordinal) && height.EndsWith("%", StringComparison.Ordinal))
            Line($"{obj}.SetSizePercent({Number(element, width.Substring(0, width.Length - 1))}, {Number(element, height.Substring(0, height.Length - 1))});");
        else if (width is not null && height is not null && !width.EndsWith("%", StringComparison.Ordinal) && !height.EndsWith("%", StringComparison.Ordinal))
            Line($"{obj}.SetSize({Number(element, width)}, {Number(element, height)});");
        else
        {
            if (width is not null) Line($"{obj}.{(width.EndsWith("%", StringComparison.Ordinal) ? "SetWidthPercent" : "SetWidth")}({Number(element, width.TrimEnd('%'))});");
            if (height is not null) Line($"{obj}.{(height.EndsWith("%", StringComparison.Ordinal) ? "SetHeightPercent" : "SetHeight")}({Number(element, height.TrimEnd('%'))});");
        }

        foreach (string pad in new[] { "PadAll", "PadLeft", "PadRight", "PadTop", "PadBottom" })
            if (Get(element, pad) is string value)
                Line($"{obj}.SetStyle{pad}({Number(element, value)});");
        foreach (string flag in new[] { "ClearFlag", "AddFlag" })
            if (Get(element, flag) is string value)
            {
                if (value is not ("HIDDEN" or "CLICKABLE" or "CHECKABLE" or "SCROLLABLE" or "EVENT_BUBBLE" or "GESTURE_BUBBLE"))
                    Fail(element, $"Unsupported flag '{value}'.");
                Line($"{obj}.{flag}(LV_OBJ_FLAG_{value});");
            }
        if (Get(element, "ScrollDirection") is string scrollDirection)
        {
            string direction = scrollDirection switch
            {
                "None" => "NONE",
                "Horizontal" => "HOR",
                "Vertical" => "VER",
                "All" => "ALL",
                _ => throw Error(element, $"Unsupported scroll direction '{scrollDirection}'.")
            };
            Line($"{obj}.SetScrollDirection(LV_DIR_{direction});");
        }
        foreach (string state in new[] { "AddState", "ClearState" })
            if (Get(element, state) is string value)
            {
                if (value is not ("CHECKED" or "PRESSED" or "DISABLED" or "ANY"))
                    Fail(element, $"Unsupported state '{value}'.");
                Line($"{obj}.{state}(LV_STATE_{value});");
            }
        if (Get(element, "ExtClickArea") is string extClickArea)
            Line($"{obj}.SetExtClickArea({Number(element, extClickArea)});");
        if (Get(element, "X") is string positionX) Line($"{obj}.SetX({Number(element, positionX)});");
        if (Get(element, "Y") is string positionY) Line($"{obj}.SetY({Number(element, positionY)});");

        string? text = Get(element, "Text");
        if (text is not null)
        {
            if (type is not ("LVLabel" or "LVCheckbox" or "LVDropdown" or "LVTextArea"))
                Fail(element, "Text requires Label, Checkbox, Dropdown, or TextArea.");
            Line($"{variable}.SetText(\"{Escape(text)}\"u8);");
        }
        if (Get(element, "LongMode") is string longMode)
        {
            RequireType(element, type, "LongMode", "LVLabel");
            Line($"{variable}.SetLongMode({BoundedNumber(element, longMode, byte.MinValue, byte.MaxValue)});");
        }
        EmitBoolean(element, type, variable, "Recolor", "LVLabel", "SetRecolor");
        string? min = Get(element, "RangeMin"), max = Get(element, "RangeMax");
        if (min is not null || max is not null)
        {
            if (type is not ("LVSlider" or "LVBar" or "LVArc") || min is null || max is null)
                throw Error(element, "RangeMin and RangeMax are required together on Slider, Bar, or Arc.");
            string minimum = type == "LVArc" ? BoundedNumber(element, min, short.MinValue, short.MaxValue) : Number(element, min);
            string maximum = type == "LVArc" ? BoundedNumber(element, max, short.MinValue, short.MaxValue) : Number(element, max);
            Line($"{variable}.SetRange({minimum}, {maximum});");
        }
        if (Get(element, "Value") is string numericValue)
        {
            if (type is not ("LVSlider" or "LVBar" or "LVArc")) Fail(element, "Value requires Slider, Bar, or Arc.");
            Line($"{variable}.SetValue({(type == "LVArc" ? BoundedNumber(element, numericValue, short.MinValue, short.MaxValue) : Number(element, numericValue))});");
        }
        if (Get(element, "StartValue") is string startValue)
        {
            RequireType(element, type, "StartValue", "LVBar");
            Line($"{variable}.SetStartValue({Number(element, startValue)});");
        }
        if (Get(element, "LeftValue") is string leftValue)
        {
            RequireType(element, type, "LeftValue", "LVSlider");
            Line($"{variable}.SetLeftValue({Number(element, leftValue)});");
        }
        if (Get(element, "Mode") is string mode)
        {
            if (type == "LVBar" && mode is "Normal" or "Symmetrical" or "Range")
                Line($"{variable}.SetMode(LV_BAR_MODE_{mode.ToUpperInvariant()});");
            else if (type != "LVRoller" || mode is not ("Normal" or "Infinite"))
                Fail(element, $"Unsupported Mode '{mode}' on {element.Name}.");
        }
        foreach (string angle in new[] { "StartAngle", "EndAngle", "BackgroundStartAngle", "BackgroundEndAngle", "Rotation" })
            if (Get(element, angle) is not null) RequireType(element, type, angle, "LVArc");
        EmitArcAngles(element, variable, "StartAngle", "EndAngle", "SetAngles");
        EmitArcAngles(element, variable, "BackgroundStartAngle", "BackgroundEndAngle", "SetBackgroundAngles");
        if (Get(element, "Rotation") is string rotation)
            Line($"{variable}.SetRotation({BoundedNumber(element, rotation, ushort.MinValue, ushort.MaxValue)});");

        if (Get(element, "Options") is string options)
        {
            if (type == "LVDropdown") Line($"{variable}.SetOptions(\"{Escape(options)}\"u8);");
            else if (type == "LVRoller")
                Line($"{variable}.SetOptions(\"{Escape(options)}\"u8, LV_ROLLER_MODE_{(Get(element, "Mode") ?? "Normal").ToUpperInvariant()});");
            else Fail(element, "Options requires Dropdown or Roller.");
        }
        else if (type == "LVRoller" && Get(element, "Mode") is not null)
            Fail(element, "Roller Mode requires Options.");
        if (Get(element, "Selected") is string selected)
        {
            if (type is not ("LVDropdown" or "LVRoller")) Fail(element, "Selected requires Dropdown or Roller.");
            Line($"{variable}.SetSelected({BoundedNumber(element, selected, ushort.MinValue, ushort.MaxValue)});");
        }
        if (Get(element, "Open") is not null)
        {
            RequireType(element, type, "Open", "LVDropdown");
            if (Boolean(element, "Open", false)) Line($"{variable}.Open();");
        }
        if (Get(element, "VisibleRowCount") is string visibleRowCount)
        {
            RequireType(element, type, "VisibleRowCount", "LVRoller");
            Line($"{variable}.SetVisibleRowCount({BoundedNumber(element, visibleRowCount, byte.MinValue, byte.MaxValue)});");
        }
        if (Get(element, "PlaceholderText") is string placeholder)
        {
            RequireType(element, type, "PlaceholderText", "LVTextArea");
            Line($"{variable}.SetPlaceholderText(\"{Escape(placeholder)}\"u8);");
        }
        EmitBoolean(element, type, variable, "OneLine", "LVTextArea", "SetOneLine");
        EmitBoolean(element, type, variable, "PasswordMode", "LVTextArea", "SetPasswordMode");
        if (Get(element, "MaxLength") is string maxLength)
        {
            RequireType(element, type, "MaxLength", "LVTextArea");
            Line($"{variable}.SetMaxLength({UnsignedNumber(element, maxLength)}u);");
        }
        if (Get(element, "CursorPosition") is string cursorPosition)
        {
            RequireType(element, type, "CursorPosition", "LVTextArea");
            Line($"{variable}.SetCursorPosition({Number(element, cursorPosition)});");
        }
        foreach (string count in new[] { "RowCount", "ColumnCount" })
            if (Get(element, count) is string value)
            {
                RequireType(element, type, count, "LVTable");
                Line($"{variable}.Set{count}({BoundedNumber(element, value, ushort.MinValue, ushort.MaxValue)});");
            }
        if (Get(element, "Checked") is not null)
        {
            if (type is not ("LVSwitch" or "LVCheckbox")) Fail(element, "Checked requires Switch or Checkbox.");
            Line($"{variable}.SetChecked({Boolean(element, "Checked", false).ToString().ToLowerInvariant()});");
        }

        string? align = Get(element, "Align"), relativeAlign = Get(element, "RelativeAlign"), relativeTo = Get(element, "RelativeTo");
        string x = Number(element, Get(element, "OffsetX") ?? "0"), y = Number(element, Get(element, "OffsetY") ?? "0");
        if (relativeTo is not null)
        {
            if (!names.Contains(relativeTo) || relativeTo == variable) Fail(element, $"Unknown or forward RelativeTo '{relativeTo}'.");
            if (relativeAlign is null || !Alignments.Contains(relativeAlign)) Fail(element, "RelativeAlign requires a valid LVAlign value.");
            Line($"{obj}.AlignTo({(namedTypes[relativeTo] == "LVObject" ? relativeTo : relativeTo + ".Object")}, LVAlign.{relativeAlign}, {x}, {y});");
        }
        else if (align is not null)
        {
            if (!Alignments.Contains(align)) Fail(element, $"Unsupported alignment '{align}'.");
            Line($"{obj}.Align(LVAlign.{align}, {x}, {y});");
        }
        else if (relativeAlign is not null || Get(element, "OffsetX") is not null || Get(element, "OffsetY") is not null)
            Fail(element, "Offsets and RelativeAlign require Align or RelativeTo.");
        if (align is not null && relativeTo is not null) Fail(element, "Align and RelativeTo cannot be combined.");
        if (Boolean(element, "UpdateLayout", false)) Line($"{obj}.UpdateLayout();");

        string? handler = Get(element, "On"), filter = Get(element, "Filter"), eventData = Get(element, "EventData");
        if (handler is not null)
        {
            Identifier(element, handler);
            if (filter is not ("All" or "Pressed" or "Pressing" or "Clicked" or "Released" or "ValueChanged" or "Ready" or "Cancel" or "Delete"))
                throw Error(element, "On requires a supported Filter value.");
            if (eventData is not null && !names.Contains(eventData)) Fail(element, $"Unknown or forward EventData '{eventData}'.");
            string filterName = filter == "ValueChanged" ? "VALUE_CHANGED" : filter.ToUpperInvariant();
            Line($"{variable}.AddEventCallback(&{handler}, LV_EVENT_{filterName}{(eventData is null ? "" : $", {eventData}.Handle")});");
        }
        else if (filter is not null || eventData is not null)
            Fail(element, "Filter and EventData require On.");

        foreach (XElement child in element.Elements())
            Emit(child, variable, type);
    }

    private void EmitStyle(XElement element, string parent, string parentType)
    {
        CheckContent(element);
        if (element.HasElements)
            Fail(element, "Style cannot contain child elements.");
        string part = Get(element, "Part") ?? "Main";
        if (part is not ("Main" or "Indicator" or "Knob"))
            Fail(element, $"Unsupported style part '{part}'.");
        foreach (XAttribute attribute in element.Attributes())
            if (attribute.Name.Namespace != XNamespace.None || attribute.Name.LocalName is not
                ("Part" or "ArcOpacity" or "ArcColor" or "BackgroundColor" or "ShadowWidth" or
                 "ShadowOpacity" or "ShadowOffsetY" or "TextFont"))
                Fail(attribute, $"Unsupported style attribute '{attribute.Name}'.");

        string target = parentType == "LVObject" ? parent : parent + ".Object";
        string selector = "LV_PART_" + part.ToUpperInvariant();
        if (Get(element, "ArcOpacity") is string arcOpacity)
            Line($"{target}.SetStyleArcOpacity({Opacity(element, arcOpacity)}, {selector});");
        if (Get(element, "ArcColor") is string arcColor)
            Line($"{target}.SetStyleArcColor({Color(element, arcColor)}, {selector});");
        if (Get(element, "BackgroundColor") is string backgroundColor)
            Line($"{target}.SetStyleBackgroundColor({Color(element, backgroundColor)}, {selector});");
        if (Get(element, "ShadowWidth") is string shadowWidth)
            Line($"{target}.SetStyleShadowWidth({Number(element, shadowWidth)}, {selector});");
        if (Get(element, "ShadowOpacity") is string shadowOpacity)
            Line($"{target}.SetStyleShadowOpacity({Opacity(element, shadowOpacity)}, {selector});");
        if (Get(element, "ShadowOffsetY") is string shadowOffsetY)
            Line($"{target}.SetStyleShadowOffsetY({Number(element, shadowOffsetY)}, {selector});");
        if (Get(element, "TextFont") is string font)
        {
            if (font != "Large") Fail(element, $"Unsupported text font '{font}'.");
            Line($"{target}.SetStyleThemeFontLarge({selector});");
        }
    }

    private string Color(XElement element, string value)
    {
        if (value.Length != 7 || value[0] != '#' ||
            !uint.TryParse(value.Substring(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint color))
            throw Error(element, $"'{value}' must be a color in #RRGGBB format.");
        return "0x" + color.ToString("X6", CultureInfo.InvariantCulture) + "u";
    }

    private string Opacity(XElement element, string value)
    {
        if (value.EndsWith("%", StringComparison.Ordinal))
        {
            if (int.TryParse(value.Substring(0, value.Length - 1), NumberStyles.None,
                    CultureInfo.InvariantCulture, out int percentage) && percentage <= 100)
                return (percentage * 255 / 100).ToString(CultureInfo.InvariantCulture);
        }
        else if (byte.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out byte opacity))
            return opacity.ToString(CultureInfo.InvariantCulture);
        throw Error(element, $"'{value}' must be an opacity from 0 to 255 or 0% to 100%.");
    }

    private void EmitTableEntry(XElement element, string table)
    {
        foreach (XAttribute attribute in element.Attributes())
        {
            if (attribute.IsNamespaceDeclaration || attribute.Name.Namespace != XNamespace.None ||
                (element.Name.LocalName == "Cell" ? attribute.Name.LocalName is not ("Row" or "Column" or "Text")
                    : attribute.Name.LocalName is not ("Index" or "Width")))
                Fail(attribute, $"Unsupported {element.Name} attribute '{attribute.Name}'.");
        }
        CheckContent(element);
        if (element.HasElements) Fail(element, $"{element.Name} cannot contain controls.");
        if (element.Name.LocalName == "Cell")
        {
            string row = BoundedNumber(element, Required(element, "Row"), ushort.MinValue, ushort.MaxValue);
            string column = BoundedNumber(element, Required(element, "Column"), ushort.MinValue, ushort.MaxValue);
            Line($"{table}.SetCellValue({row}, {column}, \"{Escape(Required(element, "Text"))}\"u8);");
        }
        else
        {
            string index = BoundedNumber(element, Required(element, "Index"), ushort.MinValue, ushort.MaxValue);
            Line($"{table}.SetColumnWidth({index}, {Number(element, Required(element, "Width"))});");
        }
    }

    private void EmitArcAngles(XElement element, string variable, string startName, string endName, string method)
    {
        string? start = Get(element, startName), end = Get(element, endName);
        if (start is null && end is null) return;
        if (start is null || end is null) throw Error(element, $"{startName} and {endName} must be specified together.");
        Line($"{variable}.{method}({BoundedNumber(element, start, ushort.MinValue, ushort.MaxValue)}, {BoundedNumber(element, end, ushort.MinValue, ushort.MaxValue)});");
    }

    private void EmitBoolean(XElement element, string type, string variable, string attribute, string requiredType, string method)
    {
        if (Get(element, attribute) is null) return;
        RequireType(element, type, attribute, requiredType);
        Line($"{variable}.{method}({Boolean(element, attribute, false).ToString().ToLowerInvariant()});");
    }

    private void RequireType(XElement element, string type, string attribute, string requiredType)
    {
        if (type != requiredType) Fail(element, $"{attribute} requires {requiredType.Substring(2)}.");
    }

    private string BoundedNumber(XElement element, string value, int minimum, int maximum)
    {
        string number = Number(element, value);
        if (int.Parse(number, CultureInfo.InvariantCulture) < minimum || int.Parse(number, CultureInfo.InvariantCulture) > maximum)
            Fail(element, $"'{value}' must be between {minimum} and {maximum}.");
        return number;
    }

    private string UnsignedNumber(XElement element, string value)
    {
        if (uint.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out uint result))
            return result.ToString(CultureInfo.InvariantCulture);
        throw Error(element, $"'{value}' must be a nonnegative 32-bit integer.");
    }

    private void Line(string text) => statements.Append("        ").AppendLine(text);
    private static string? Get(XElement element, string name) => (string?)element.Attribute(name);
    private string Required(XElement element, XName name) => (string?)element.Attribute(name) ?? throw Error(element, $"Missing '{name}'.");
    private static string Escape(string value) => value.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", "\\r").Replace("\n", "\\n");
    private bool Boolean(XElement element, XName name, bool fallback)
    {
        string? value = (string?)element.Attribute(name);
        if (value is null) return fallback;
        if (bool.TryParse(value, out bool result)) return result;
        throw Error(element, $"'{name}' must be true or false.");
    }
    private string Number(XElement element, string value)
    {
        if (int.TryParse(value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out int result))
            return result.ToString(CultureInfo.InvariantCulture);
        throw Error(element, $"'{value}' must be an integer.");
    }
    private void Identifier(XElement element, string value)
    {
        if (value.Length == 0 || !(char.IsLetter(value[0]) || value[0] == '_') || value.Any(character => !(char.IsLetterOrDigit(character) || character == '_'))
            || new[] { "class", "event", "new", "object", "string", "int", "bool", "void", "private", "static", "default", "base", "this" }.Contains(value))
            Fail(element, $"Invalid C# identifier '{value}'.");
    }
    private void CheckContent(XElement element)
    {
        if (element.Nodes().OfType<XText>().Any(text => !string.IsNullOrWhiteSpace(text.Value)))
            Fail(element, "Text nodes are unsupported; use the Text attribute.");
    }
    private void Fail(XObject node, string message) => throw Error(node, message);
    private XamlGenerationException Error(XObject node, string message)
    {
        IXmlLineInfo location = (IXmlLineInfo)node;
        return new XamlGenerationException(message, location.LineNumber, location.LinePosition);
    }
}

internal sealed class XamlGenerationException : Exception
{
    public int LineNumber { get; }
    public int ColumnNumber { get; }

    public XamlGenerationException(string message, int lineNumber, int columnNumber) : base(message)
    {
        LineNumber = lineNumber;
        ColumnNumber = columnNumber;
    }
}
