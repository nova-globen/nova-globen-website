namespace Fravashi.Broker.Configuration;

/// <summary>
/// Thrown when the broker's security configuration is absent or incoherent at startup. A broker that cannot be
/// configured coherently <b>must not run</b> (fail closed): the message names what is wrong (never a
/// secret value), and <c>Program</c> turns it into a loud refusal-to-serve and a non-zero exit.
/// </summary>
public sealed class BrokerConfigurationException(string message) : Exception(message);
