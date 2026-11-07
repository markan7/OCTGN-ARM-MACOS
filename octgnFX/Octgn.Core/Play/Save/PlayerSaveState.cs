using System;
#if NETFRAMEWORK
using System.Windows.Media;
#else
using Octgn.Core.Media;
#endif

namespace Octgn.Core.Play.Save
{
    [Serializable]
    public class PlayerSaveState : IPlayerSaveState
    {
        public byte Id { get; set; }
        public string Nickname { get; set; }
        public Color Color { get; set; }
    }
}