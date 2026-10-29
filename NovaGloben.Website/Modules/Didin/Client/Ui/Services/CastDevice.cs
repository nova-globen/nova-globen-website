using System;
using System.Threading.Tasks;
using Didin.Shared.Models;

namespace Didin.Module.Media.Services
{
    /// <summary>What the cast device is doing, as the player shows it.</summary>
    public class CastStatus
    {
        public bool Available { get; set; }     // a Chromecast / Google TV is on the network
        public bool Connected { get; set; }     // a cast session is open
        public string Device { get; set; }      // its name, "Living room TV"
        public string FileToken { get; set; }   // the file it was given (null until one is loaded)
        public double Position { get; set; }
        public double Duration { get; set; }
        public bool Paused { get; set; }
        public bool Ended { get; set; }         // played to its end (not stopped)
    }

    /// <summary>
    /// Cast to TV: the player hands the film to a Chromecast or Google TV, which fetches it itself (a cast
    /// ticket, api/cast), and then acts as its remote. Each host brings its own: the website Google's Cast SDK in Chrome,
    /// the Android app the native Cast framework; hosts that cannot cast keep <see cref="NoCastDevice"/>.
    /// </summary>
    public interface ICastDevice
    {
        CastStatus Status { get; }
        event Action Changed;

        /// <summary>Starts looking for devices; harmless to call again.</summary>
        Task InitAsync();

        /// <summary>
        /// Plays a file on the device from <paramref name="position"/> seconds, with subtitle <paramref name="track"/>
        /// (an index into <c>media.Subtitles</c>) on. Asks the user to pick a device first unless one is connected.
        /// </summary>
        Task StartAsync(string fileToken, CastMedia media, string title, double position, int? track);

        Task PlayPauseAsync();
        Task SeekAsync(double position);
        Task SetTrackAsync(int? track);

        /// <summary>Stops the film on the device and leaves it.</summary>
        Task StopAsync();
    }

    /// <summary>A host that cannot cast (the Windows app, a TV): the player shows no cast button.</summary>
    public class NoCastDevice : ICastDevice
    {
        public CastStatus Status { get; } = new();
        public event Action Changed { add { } remove { } }
        public Task InitAsync() => Task.CompletedTask;
        public Task StartAsync(string fileToken, CastMedia media, string title, double position, int? track) => Task.CompletedTask;
        public Task PlayPauseAsync() => Task.CompletedTask;
        public Task SeekAsync(double position) => Task.CompletedTask;
        public Task SetTrackAsync(int? track) => Task.CompletedTask;
        public Task StopAsync() => Task.CompletedTask;
    }
}
