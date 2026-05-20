using System.Collections.ObjectModel;
using Avalonia.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EAPSimulator.Core.Protocols.SecsGem.SecsII;

namespace EAPSimulator.UI.ViewModels;

/// <summary>
/// Tree node representing a SECS-II item. Supports inline editing.
/// </summary>
public partial class SecsItemViewModel : ObservableObject
{
    [ObservableProperty]
    private string _typeName = "A";

    [ObservableProperty]
    private string _valueText = string.Empty;

    [ObservableProperty]
    private bool _isExpanded = true;

    // ─── Field Metadata ───

    [ObservableProperty]
    private string _alias = string.Empty;

    [ObservableProperty]
    private string _description = string.Empty;

    [ObservableProperty]
    private string _format = string.Empty;

    [ObservableProperty]
    private string _nlb = string.Empty;

    [ObservableProperty]
    private string _defaultValue = string.Empty;

    /// <summary>
    /// Value mapping: maps raw value to display text.
    /// e.g. "0" -> "OFF", "1" -> "ON"
    /// </summary>
    public ObservableCollection<ValueMappingEntry> ValueMappings { get; } = new();

    /// <summary>
    /// Display text for the tree node. Shows alias if set, otherwise type and value.
    /// </summary>
    public string DisplayText
    {
        get
        {
            if (!string.IsNullOrEmpty(Alias))
            {
                return IsList ? Alias : $"{Alias}: {ValueText}";
            }
            return IsList ? $"L[{Children.Count}]" : $"{TypeName} {ValueText}";
        }
    }

    /// <summary>
    /// Tooltip text showing full field information.
    /// </summary>
    public string ToolTipText
    {
        get
        {
            var parts = new List<string>();
            if (!string.IsNullOrEmpty(Alias)) parts.Add($"Name: {Alias}");
            if (!string.IsNullOrEmpty(Description)) parts.Add($"Desc: {Description}");
            if (!string.IsNullOrEmpty(Format)) parts.Add($"Format: {Format}");
            if (!string.IsNullOrEmpty(Nlb)) parts.Add($"NLB: {Nlb}");
            if (!string.IsNullOrEmpty(DefaultValue)) parts.Add($"Default: {DefaultValue}");
            if (ValueMappings.Count > 0) parts.Add($"Mappings: {ValueMappings.Count}");
            return parts.Count > 0 ? string.Join("\n", parts) : $"{TypeName} {ValueText}";
        }
    }

    partial void OnAliasChanged(string value)
    {
        OnPropertyChanged(nameof(DisplayText));
        OnPropertyChanged(nameof(ToolTipText));
    }

    partial void OnDescriptionChanged(string value) => OnPropertyChanged(nameof(ToolTipText));
    partial void OnFormatChanged(string value) => OnPropertyChanged(nameof(ToolTipText));
    partial void OnNlbChanged(string value) => OnPropertyChanged(nameof(ToolTipText));
    partial void OnDefaultValueChanged(string value) => OnPropertyChanged(nameof(ToolTipText));

    public SecsItemViewModel? Parent { get; set; }
    public ObservableCollection<SecsItemViewModel> Children { get; } = new();

    public static string[] TypeNames { get; } =
        ["L", "A", "B", "Boolean", "U1", "U2", "U4", "U8", "I1", "I2", "I4", "I8", "F4", "F8"];

    public bool IsList => TypeName == "L";

    partial void OnTypeNameChanged(string value)
    {
        OnPropertyChanged(nameof(IsList));
        OnPropertyChanged(nameof(DisplayText));
        if (value == "L")
            ValueText = "";
        else if (string.IsNullOrEmpty(ValueText))
            ValueText = "0";
    }

    partial void OnValueTextChanged(string value)
    {
        OnPropertyChanged(nameof(DisplayText));
    }

    public SecsItemViewModel()
    {
        Children.CollectionChanged += (_, _) => OnPropertyChanged(nameof(IsList));
    }

    public SecsItemViewModel(string typeName, string valueText = "") : this()
    {
        TypeName = typeName;
        ValueText = valueText;
    }

    public static SecsItemViewModel FromSecsItem(SecsItem item) => item switch
    {
        SecsList list => FromList(list),
        SecsAscii a => new("A", a.Value),
        SecsBinary b => new("B", BitConverter.ToString(b.Value).Replace("-", "")),
        SecsBoolean bl => new("Boolean", bl.Value ? "1" : "0"),
        SecsU1 u1 => new("U1", string.Join(",", u1.Value)),
        SecsU2 u2 => new("U2", string.Join(",", u2.Value)),
        SecsU4 u4 => new("U4", string.Join(",", u4.Value)),
        SecsU8 u8 => new("U8", string.Join(",", u8.Value)),
        SecsI1 i1 => new("I1", string.Join(",", i1.Value)),
        SecsI2 i2 => new("I2", string.Join(",", i2.Value)),
        SecsI4 i4 => new("I4", string.Join(",", i4.Value)),
        SecsI8 i8 => new("I8", string.Join(",", i8.Value)),
        SecsF4 f4 => new("F4", string.Join(",", f4.Value)),
        SecsF8 f8 => new("F8", string.Join(",", f8.Value)),
        _ => new("A", item.ToString())
    };

    private static SecsItemViewModel FromList(SecsList list)
    {
        var vm = new SecsItemViewModel("L");
        foreach (var child in list.Items)
        {
            var childVm = FromSecsItem(child);
            childVm.Parent = vm;
            vm.Children.Add(childVm);
        }
        return vm;
    }

    public SecsItem ToSecsItem()
    {
        if (IsList)
            return SecsItem.L(Children.Select(c => c.ToSecsItem()).ToArray());

        var val = ValueText.Trim();
        return TypeName switch
        {
            "A" => SecsItem.A(val),
            "B" => SecsItem.B(ParseHexBytes(val)),
            "Boolean" => SecsItem.Boolean(val == "1" || val.Equals("true", StringComparison.OrdinalIgnoreCase)),
            "U1" => SecsItem.U1(ParseArray<byte>(val)),
            "U2" => SecsItem.U2(ParseArray<ushort>(val)),
            "U4" => SecsItem.U4(ParseArray<uint>(val)),
            "U8" => SecsItem.U8(ParseArray<ulong>(val)),
            "I1" => SecsItem.I1(ParseArray<sbyte>(val)),
            "I2" => SecsItem.I2(ParseArray<short>(val)),
            "I4" => SecsItem.I4(ParseArray<int>(val)),
            "I8" => SecsItem.I8(ParseArray<long>(val)),
            "F4" => SecsItem.F4(ParseArray<float>(val)),
            "F8" => SecsItem.F8(ParseArray<double>(val)),
            _ => SecsItem.A(val),
        };
    }

    private static T[] ParseArray<T>(string csv) where T : struct
    {
        if (string.IsNullOrWhiteSpace(csv)) return [];
        return csv.Split(',').Select(s => (T)Convert.ChangeType(s.Trim(), typeof(T))).ToArray();
    }

    private static byte[] ParseHexBytes(string hex)
    {
        hex = hex.Replace(" ", "").Replace("-", "");
        if (hex.Length % 2 != 0) hex = "0" + hex;
        var bytes = new byte[hex.Length / 2];
        for (int i = 0; i < bytes.Length; i++)
            bytes[i] = Convert.ToByte(hex.Substring(i * 2, 2), 16);
        return bytes;
    }

    public SecsItemViewModel Clone()
    {
        var clone = new SecsItemViewModel(TypeName, ValueText)
        {
            Alias = Alias,
            Description = Description,
            Format = Format,
            Nlb = Nlb,
            DefaultValue = DefaultValue,
        };
        foreach (var mapping in ValueMappings)
            clone.ValueMappings.Add(mapping.Clone());
        foreach (var child in Children)
        {
            var childClone = child.Clone();
            childClone.Parent = clone;
            clone.Children.Add(childClone);
        }
        return clone;
    }

    [RelayCommand]
    private void AddChild(string? typeName)
    {
        var tn = typeName ?? "A";
        if (!IsList) return;
        var child = new SecsItemViewModel(tn, tn == "L" ? "" : "0") { Parent = this };
        Children.Add(child);
        IsExpanded = true;
    }

    [RelayCommand]
    private void AddSibling(string? typeName)
    {
        if (Parent == null) return;
        var tn = typeName ?? "A";
        var sibling = new SecsItemViewModel(tn, tn == "L" ? "" : "0") { Parent = Parent };
        var idx = Parent.Children.IndexOf(this);
        Parent.Children.Insert(idx + 1, sibling);
    }

    [RelayCommand]
    private void DeleteThis() => Parent?.Children.Remove(this);

    [RelayCommand]
    private void CopyThis()
    {
        if (Parent == null) return;
        var cloned = Clone();
        cloned.Parent = Parent;
        var idx = Parent.Children.IndexOf(this);
        Parent.Children.Insert(idx + 1, cloned);
    }

    [RelayCommand]
    private void MoveUp()
    {
        if (Parent == null) return;
        var idx = Parent.Children.IndexOf(this);
        if (idx > 0) Parent.Children.Move(idx, idx - 1);
    }

    [RelayCommand]
    private void MoveDown()
    {
        if (Parent == null) return;
        var idx = Parent.Children.IndexOf(this);
        if (idx < Parent.Children.Count - 1) Parent.Children.Move(idx, idx + 1);
    }

    /// <summary>
    /// Open the field edit dialog. Called from code-behind.
    /// </summary>
    public async Task EditFieldAsync(Window owner)
    {
        var dialog = new Views.FieldEditDialog(this);
        await dialog.ShowDialog<bool>(owner);
        // Refresh display after dialog closes
        OnPropertyChanged(nameof(DisplayText));
        OnPropertyChanged(nameof(ToolTipText));
    }
}

/// <summary>
/// Represents a value mapping entry (raw value -> display text).
/// </summary>
public partial class ValueMappingEntry : ObservableObject
{
    [ObservableProperty]
    private string _value = string.Empty;

    [ObservableProperty]
    private string _displayText = string.Empty;

    public ValueMappingEntry Clone() => new() { Value = Value, DisplayText = DisplayText };
}
