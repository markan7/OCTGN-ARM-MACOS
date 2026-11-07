namespace Octgn.Core.Play
{
#if NETFRAMEWORK
    using System.Windows.Media;
#else
    using Octgn.Core.Media;
#endif

    public interface IPlayPlayer
    {
		/// <summary>
		/// Identifier
		/// </summary>
        byte Id{ get; }

		/// <summary>
		/// Nickname
		/// </summary>
        string Name{ get; }

		/// <summary>
		/// Player Color
		/// </summary>
        Color Color { get; }

		/// <summary>
		/// Player State
		/// </summary>
        PlayerState State { get; }
    }
}