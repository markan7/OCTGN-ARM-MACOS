#if NETFRAMEWORK
using System.Windows.Media;
#else
using Octgn.Core.Media;
#endif

namespace Octgn.Core.Play.Save
{
    public interface IPlayerSaveState
    {
        byte Id { get; }
        string Nickname { get; }
        Color Color { get; }
    }
}
