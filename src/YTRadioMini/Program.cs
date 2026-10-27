using YTRadioMini.Mocks;
using YTRadioMini.UI;

namespace YTRadioMini;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();

        // 阶段 0：使用模拟控制器；集成阶段（阶段 2）替换为真实 RadioController。
        var radio = new FakeRadioController();
        Application.Run(new MainForm(radio));
    }
}
