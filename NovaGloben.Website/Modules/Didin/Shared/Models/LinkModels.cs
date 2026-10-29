using System;

namespace Didin.Shared.Models
{
    /// <summary>
    /// Signing in a TV, a desktop browser or the Windows app from a phone: the device shows a QR code and a short
    /// code, a phone that is signed in approves it, and the device signs in. This is what the phone sees before it
    /// approves.
    /// </summary>
    public class LinkInfo
    {
        public string Code { get; set; }          // "ABCD-2345"
        public string Status { get; set; }        // pending | approved | denied | used | expired
        public string Device { get; set; }        // "Chrome on a Samsung TV", "Didin for Windows"
        public bool SameNetwork { get; set; }     // the device and this phone reach the server from one address
        public int AgeSeconds { get; set; }       // since the device showed the code
    }

    public class LinkDecision
    {
        public bool Approve { get; set; }
    }
}
