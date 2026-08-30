using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;

namespace BalanceIsland.Windows;

public partial class EnvironmentImportWindow : Window
{
    private readonly EnvironmentImportRow[] _rows;

    public EnvironmentImportWindow(
        IEnumerable<EnvironmentCredentialCandidate> candidates,
        IEnumerable<Account> existingAccounts)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        ArgumentNullException.ThrowIfNull(existingAccounts);

        InitializeComponent();
        var importedVariables = existingAccounts
            .Where(account => account.CredentialSource == CredentialSource.EnvironmentVariable)
            .Select(account => $"{account.Provider}\0{account.EnvironmentVariableName}")
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        _rows = candidates
            .Select(candidate => new EnvironmentImportRow(
                candidate,
                candidate.Provider is { } provider &&
                importedVariables.Contains($"{provider}\0{candidate.VariableName}")))
            .ToArray();
        CandidatesGrid.ItemsSource = _rows;
    }

    public IReadOnlyList<EnvironmentCredentialCandidate> SelectedCandidates { get; private set; } = [];

    private void Import_Click(object sender, RoutedEventArgs e)
    {
        SelectedCandidates = _rows
            .Where(row => row.IsSelected && !row.IsAlreadyImported)
            .Select(row => row.ResolvedCandidate)
            .ToArray();
        DialogResult = true;
    }
}

public sealed class EnvironmentImportRow : INotifyPropertyChanged
{
    private bool _isSelected;
    private Provider? _selectedProvider;

    public EnvironmentImportRow(EnvironmentCredentialCandidate candidate, bool isAlreadyImported)
    {
        Candidate = candidate;
        IsAlreadyImported = isAlreadyImported;
        SelectedProvider = candidate.Provider;
    }

    internal EnvironmentCredentialCandidate Candidate { get; }
    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            var next = CanSelect && value;
            if (_isSelected == next) return;
            _isSelected = next;
            OnPropertyChanged();
        }
    }
    public bool IsAlreadyImported { get; }
    public IReadOnlyList<ProviderDefinition> ProviderOptions => ProviderCatalog.All;
    public Provider? SelectedProvider
    {
        get => _selectedProvider;
        set
        {
            if (_selectedProvider == value) return;
            _selectedProvider = value;
            if (!CanSelect) _isSelected = false;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CanSelect));
            OnPropertyChanged(nameof(IsSelected));
            OnPropertyChanged(nameof(State));
        }
    }
    public bool CanSelect => !IsAlreadyImported && SelectedProvider is not null;
    public EnvironmentCredentialCandidate ResolvedCandidate => Candidate.WithProvider(
        SelectedProvider ?? throw new InvalidOperationException("必须先选择 Provider。"));
    public string VariableName => Candidate.VariableName;
    public string Scope => Candidate.Scope;
    public string MaskedKey => Candidate.MaskedKey;
    public string MatchReason => Candidate.MatchReason;
    public string State => IsAlreadyImported
        ? "已导入"
        : SelectedProvider is null ? "请选择 Provider" : "可导入";

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
