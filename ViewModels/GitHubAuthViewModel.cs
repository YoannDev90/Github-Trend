using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows.Input;
using Github_Trend.Localization;
using Serilog;

namespace Github_Trend;

public sealed class GitHubAuthViewModel : INotifyPropertyChanged
{
    private readonly GitHubAuthenticationService _authService;
    private readonly Action<string, object?[]> _setStatusMessage;

    private object?[] _accountSummaryArgs = Array.Empty<object?>();
    private string _accountSummaryKey;
    private object?[] _authStatusArgs = Array.Empty<object?>();
    private string _authStatusKey;
    private string? _authStatusOverride;
    private string _deviceCode = string.Empty;
    private bool _isAuthenticating;
    private bool _isInitializing;

    public GitHubAuthViewModel(
        GitHubAuthenticationService authService,
        Action<string, object?[]> setStatusMessage
    )
    {
        _authService = authService;
        _setStatusMessage = setStatusMessage;

        _authStatusKey = nameof(LocalizationService.GitHubAuthNotConfigured);
        _accountSummaryKey = nameof(LocalizationService.GitHubAuthNoAccountConnected);

        SignInCommand = new RelayCommand(
            _ => ExecuteSignIn(),
            _ => !_isInitializing && !_isAuthenticating
        );
        SignOutCommand = new RelayCommand(
            _ => ExecuteSignOut(),
            _ => IsConnected && !_isAuthenticating
        );
        RefreshSessionCommand = new RelayCommand(
            _ => ExecuteRefreshSession(),
            _ => IsConnected && !_isAuthenticating
        );
        CopyDeviceCodeCommand = new RelayCommand(
            _ => { Log.Debug("CopyDeviceCode: starting"); DeviceCodeCopyRequested?.Invoke(this, EventArgs.Empty); },
            _ => HasDeviceCode
        );

        _authService.SessionChanged += (_, _) => UpdateAuthState();
    }

    public ICommand SignInCommand { get; }
    public ICommand SignOutCommand { get; }
    public ICommand RefreshSessionCommand { get; }
    public ICommand CopyDeviceCodeCommand { get; }

    public event EventHandler? DeviceCodeCopyRequested;
    public event PropertyChangedEventHandler? PropertyChanged;

    public Func<Task<bool>>? ConfirmSignOutAsync { get; set; }

    public bool IsConnected => _authService.IsConnected;

    public bool IsAuthenticating
    {
        get => _isAuthenticating;
        private set
        {
            if (_isAuthenticating == value) return;
            _isAuthenticating = value;
            OnPropertyChanged();
            RaiseCommandStateChanged();
        }
    }

    public string DeviceCode
    {
        get => _deviceCode;
        set
        {
            if (_deviceCode == value) return;
            _deviceCode = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasDeviceCode));
            (CopyDeviceCodeCommand as RelayCommand)?.RaiseCanExecuteChanged();
        }
    }

    public bool HasDeviceCode => !string.IsNullOrWhiteSpace(_deviceCode);

    public string AuthStatus =>
        _authStatusOverride
        ?? Localization.Localization.Instance.GetString(_authStatusKey, _authStatusArgs);

    public string AccountSummary =>
        Localization.Localization.Instance.GetString(_accountSummaryKey, _accountSummaryArgs);

    public void SetInitializing(bool value)
    {
        _isInitializing = value;
        RaiseCommandStateChanged();
    }

    public async Task InitializeAsync()
    {
        await _authService.InitializeAsync();
        UpdateAuthState();
    }

    public void UpdateAuthState(GitHubAuthSession? session = null)
    {
        session ??= _authService.CurrentSession;

        if (session is null)
        {
            SetAuthStatus(nameof(LocalizationService.GitHubAuthNotConnected));
            SetAccountSummary(nameof(LocalizationService.GitHubAuthNoAccountLinked));
        }
        else
        {
            SetAuthStatus(nameof(LocalizationService.GitHubAuthConnected), session.Summary);
            SetAccountSummary(
                nameof(LocalizationService.GitHubAuthLinkedAccount),
                session.DisplayName,
                session.Login
            );
        }

        OnPropertyChanged(nameof(IsConnected));
        RaiseCommandStateChanged();
    }

    public void RaiseCommandStateChanged()
    {
        (SignInCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (SignOutCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (RefreshSessionCommand as RelayCommand)?.RaiseCanExecuteChanged();
    }

    private void ExecuteSignIn() => _ = SignInAsync();
    private void ExecuteSignOut() => _ = SignOutAsync();
    private void ExecuteRefreshSession() => _ = RefreshSessionAsync();

    private async Task SignInAsync()
    {
        Log.Debug("SignIn: starting");
        if (_isInitializing || _isAuthenticating) return;
        try
        {
            IsAuthenticating = true;
            DeviceCode = string.Empty;
            SetAuthStatusRaw(
                Localization.Localization.Instance.GetString(
                    nameof(LocalizationService.StatusGitHubSignInStarting)
                )
            );
            var session = await _authService.BeginInteractiveSignInAsync(
                SetAuthStatusRaw,
                code =>
                {
                    DeviceCode = code;
                    DeviceCodeCopyRequested?.Invoke(this, EventArgs.Empty);
                }
            );
            UpdateAuthState(session);
            _setStatusMessage(
                nameof(LocalizationService.StatusGitHubSignInSuccess),
                new object?[] { session?.Summary ?? string.Empty }
            );
        }
        catch (Exception ex)
        {
            ClearAuthStatusOverride();
            SetAuthStatus(nameof(LocalizationService.StatusGitHubSignInFailed), ex.Message);
            _setStatusMessage(
                nameof(LocalizationService.StatusGitHubSignInFailed),
                new object?[] { ex.Message }
            );
        }
        finally
        {
            IsAuthenticating = false;
            DeviceCode = string.Empty;
            ClearAuthStatusOverride();
            RaiseCommandStateChanged();
        }
    }

    private async Task SignOutAsync()
    {
        Log.Debug("SignOut: starting");
        if (ConfirmSignOutAsync != null)
        {
            var confirmed = await ConfirmSignOutAsync();
            if (!confirmed)
            {
                Log.Debug("SignOut: cancelled by user");
                return;
            }
        }

        await _authService.SignOutAsync();
        UpdateAuthState();
        _setStatusMessage(nameof(LocalizationService.StatusGitHubSignedOut), Array.Empty<object?>());
    }

    private async Task RefreshSessionAsync()
    {
        Log.Debug("RefreshSession: starting");
        try
        {
            IsAuthenticating = true;
            SetAuthStatus(nameof(LocalizationService.StatusGitHubRefreshStarting));
            await _authService.RefreshCurrentAsync();
            UpdateAuthState();
            _setStatusMessage(
                nameof(LocalizationService.StatusGitHubRefreshSuccess),
                Array.Empty<object?>()
            );
        }
        catch (Exception ex)
        {
            SetAuthStatus(nameof(LocalizationService.StatusGitHubRefreshFailed), ex.Message);
            _setStatusMessage(
                nameof(LocalizationService.StatusGitHubRefreshFailed),
                new object?[] { ex.Message }
            );
        }
        finally
        {
            IsAuthenticating = false;
            RaiseCommandStateChanged();
        }
    }

    private void SetAuthStatus(string key, params object?[] args)
    {
        _authStatusOverride = null;
        _authStatusKey = key;
        _authStatusArgs = args;
        OnPropertyChanged(nameof(AuthStatus));
    }

    private void SetAuthStatusRaw(string message)
    {
        _authStatusOverride = message;
        OnPropertyChanged(nameof(AuthStatus));
    }

    private void ClearAuthStatusOverride()
    {
        if (_authStatusOverride is null) return;
        _authStatusOverride = null;
        OnPropertyChanged(nameof(AuthStatus));
    }

    private void SetAccountSummary(string key, params object?[] args)
    {
        _accountSummaryKey = key;
        _accountSummaryArgs = args;
        OnPropertyChanged(nameof(AccountSummary));
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
