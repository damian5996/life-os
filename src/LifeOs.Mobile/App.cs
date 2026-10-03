using LifeOs.Mobile.Views;
namespace LifeOs.Mobile;

public sealed class App(MainPage page) : Application
{
    protected override Window CreateWindow(IActivationState? activationState) => new(page);
}
