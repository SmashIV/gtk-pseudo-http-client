using Gtk;

namespace Consulta_DNI.REST;

public class Program
{
    public static void Main(string[] args)
    {
        Application.Init();
        new MainWindow();
        Application.Run();
    }
}