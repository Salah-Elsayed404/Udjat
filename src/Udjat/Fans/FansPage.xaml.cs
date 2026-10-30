using System.Windows.Controls;

namespace Udjat.Fans;

/// <summary>The Fans page: every fan, grouped by device. Bound to <see cref="Dashboard.MainView"/> through the window.</summary>
internal partial class FansPage : UserControl
{
    public FansPage() => InitializeComponent();
}
