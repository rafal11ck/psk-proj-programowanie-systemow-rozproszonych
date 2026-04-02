using Spectre.Console.Rendering;
using password_break_server.Models;

namespace password_break_server.Services;

public interface IDisplayRenderer
{
    IRenderable BuildDisplay(DisplayState state, int width, int height);
}
