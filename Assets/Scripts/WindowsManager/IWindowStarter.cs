public interface IWindowStarter
{
    string GetGroup();
    string GetName();

    void SetupModels(ViewController viewController);
}