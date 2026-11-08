#nullable disable warnings
namespace Vectorier.Gameplay.Models;

public class ModelEvent<T> where T : class
{
    private bool _bubbles;

    private bool _cancelable;

    public T Data { get; }

    public ModelEvent(T Data = null, bool Bubbles = false, bool Cancelable = false)
    {
        this.Data = Data;
        _bubbles = Bubbles;
        _cancelable = Cancelable;
    }
}
