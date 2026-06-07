using DebtManager.Application.Identity;
using DebtManager.Application.UseCases;
using DebtManager.Desktop.Services;
using System.Windows.Input;

namespace DebtManager.Desktop.ViewModels;

public sealed class UserMenuViewModel : ObservableObject
{
    private readonly WhoAmIHandler _whoAmIHandler;
    private readonly ListVaultUsersHandler _listUsersHandler;
    private readonly IToastService? _toastService;
    private readonly Action? _onLogout;
    private readonly Action? _onSwitchUser;

    public UserMenuViewModel(
        WhoAmIHandler whoAmIHandler,
        ListVaultUsersHandler listUsersHandler,
        IToastService? toastService = null,
        Action? onLogout = null,
        Action? onSwitchUser = null)
    {
        _whoAmIHandler = whoAmIHandler;
        _listUsersHandler = listUsersHandler;
        _toastService = toastService;
        _onLogout = onLogout;
        _onSwitchUser = onSwitchUser;

        LogoutCommand = new RelayCommand(() => _onLogout?.Invoke());
        SwitchUserCommand = new RelayCommand(() => _onSwitchUser?.Invoke());
    }

    private string _currentUserDisplayName = string.Empty;
    public string CurrentUserDisplayName
    {
        get => _currentUserDisplayName;
        set => SetProperty(ref _currentUserDisplayName, value);
    }

    private string _currentRoleName = string.Empty;
    public string CurrentRoleName
    {
        get => _currentRoleName;
        set => SetProperty(ref _currentRoleName, value);
    }

    private IReadOnlyList<string> _permissions = Array.Empty<string>();
    public IReadOnlyList<string> Permissions
    {
        get => _permissions;
        set => SetProperty(ref _permissions, value);
    }

    public ICommand LogoutCommand { get; }
    public ICommand SwitchUserCommand { get; }

    public async Task LoadCurrentUserAsync(Guid userId)
    {
        try
        {
            var whoAmI = await _whoAmIHandler.HandleAsync(userId, CancellationToken.None);
            if (whoAmI != null)
            {
                CurrentUserDisplayName = whoAmI.DisplayName;
                CurrentRoleName = whoAmI.RoleCode;
                Permissions = whoAmI.Permissions;
            }
        }
        catch
        {
            CurrentUserDisplayName = "Unknown";
        }
    }
}
