namespace Aiko.Application.Installation;

/// <summary>
/// A run stopped by a decision rather than by a fault: the release does not vouch for the file, the layout
/// is not what a release promises, and so on.
/// </summary>
/// <remarks>
/// A type of its own because the engine has to tell a refusal from a crash before it can report either.
/// <see cref="InstallationOutcome.Refused"/> means "nothing was changed, and here is why" - a decision worth
/// printing; anything else escaping the engine is a failure, and reporting a failure as a decision would
/// tell the user their installation is fine when it was never touched.
/// </remarks>
/// <param name="message">What was refused, and why, in the words a person reads.</param>
public sealed class InstallationRefusedException(string message) : Exception(message);
