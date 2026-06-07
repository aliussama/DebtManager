using DebtManager.Application.Identity;
using DebtManager.Application.UseCases;
using DebtManager.Desktop.Services;
using System.Windows.Input;

namespace DebtManager.Desktop.ViewModels;

public sealed class LoginViewModel : ObservableObject
{
    private readonly LoginHandler _loginHandler;
    private readonly LogoutHandler _logoutHandler;
    private readonly WhoAmIHandler _whoAmIHandler;
    private readonly Guid _deviceId;
    private readonly IToastService? _toastService;
    private readonly Action<LoginResultDto>? _onLoginSuccess;
    private readonly Action? _onLogout;

    public LoginViewModel(
        LoginHandler loginHandler,
        LogoutHandler logoutHandler,
        WhoAmIHandler whoAmIHandler,
        Guid deviceId,
        IToastService? toastService = null,
        Action<LoginResultDto>? onLoginSuccess = null,
        Action? onLogout = null)
    {
        _loginHandler = loginHandler;
        _logoutHandler = logoutHandler;
        _whoAmIHandler = whoAmIHandler;
        _deviceId = deviceId;
        _toastService = toastService;
        _onLoginSuccess = onLoginSuccess;
        _onLogout = onLogout;

        LoginCommand = new AsyncRelayCommand(DoLoginAsync);
        LogoutCommand = new AsyncRelayCommand(DoLogoutAsync);
    }

    private string _username = string.Empty;
    public string Username
    {
        get => _username;
        set => SetProperty(ref _username, value);
    }

    private string _password = string.Empty;
    public string Password
    {
        get => _password;
        set => SetProperty(ref _password, value);
    }

    private bool _rememberMe;
    public bool RememberMe
    {
        get => _rememberMe;
        set => SetProperty(ref _rememberMe, value);
    }

    private string _errorMessage = string.Empty;
    public string ErrorMessage
    {
        get => _errorMessage;
        set => SetProperty(ref _errorMessage, value);
    }

    private bool _isLoading;
    public bool IsLoading
    {
        get => _isLoading;
        set => SetProperty(ref _isLoading, value);
    }

    private Guid? _currentSessionId;
    public Guid? CurrentSessionId
    {
        get => _currentSessionId;
        set => SetProperty(ref _currentSessionId, value);
    }

    public ICommand LoginCommand { get; }
    public ICommand LogoutCommand { get; }

    private async Task DoLoginAsync()
    {
        ErrorMessage = string.Empty;
        if (string.IsNullOrWhiteSpace(Username) || string.IsNullOrWhiteSpace(Password))
        {
            ErrorMessage = "Username and password are required.";
            return;
        }

        IsLoading = true;
        try
        {
            var cmd = new LoginCommand(Username, Password, _deviceId, "1.0.0", DateOnly.FromDateTime(DateTime.Today));
            var result = await _loginHandler.HandleAsync(cmd, Guid.Empty, CancellationToken.None);

            if (!result.Success)
            {
                ErrorMessage = result.ErrorMessage ?? "Login failed.";
                return;
            }

            CurrentSessionId = result.SessionId;
            Password = string.Empty;
            _toastService?.Success($"Welcome, {Username}!");
            _onLoginSuccess?.Invoke(result);
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Login error: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    private async Task DoLogoutAsync()
    {
        if (CurrentSessionId == null) return;

        try
        {
            var cmd = new LogoutCommand(CurrentSessionId.Value, DateOnly.FromDateTime(DateTime.Today));
            await _logoutHandler.HandleAsync(cmd, Guid.Empty, _deviceId, CancellationToken.None);

            CurrentSessionId = null;
            _toastService?.Success("Logged out.");
            _onLogout?.Invoke();
        }
        catch (Exception ex)
        {
            _toastService?.Error($"Logout error: {ex.Message}");
        }
    }
}
