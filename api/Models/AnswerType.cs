namespace CampusFacilities.Api.Models;

/// <summary>
/// How a client must render the answer control for a <see cref="ClarificationQuestion"/>.
///
/// THIS IS THE REASON THERE IS NO CHAT INTERFACE. Every clarification the system asks for
/// comes back through one of these three bounded controls — a yes/no toggle, a picker over
/// a fixed option list, or a short capped string. None of them is a message box, so there
/// is no free-text turn for a conversation to grow out of. Adding a "FreeText" member here
/// would quietly turn this project into the chatbot it deliberately is not.
///
/// Persisted as a string in PostgreSQL and serialised by name over JSON, same as
/// <see cref="Role"/> and <see cref="WorkflowState"/>.
///
/// The names differ in case from the agent service's own snake_case values
/// ("yes_no", "single_select", "short_text"); the translation happens once, in
/// AgentRunResponse, and nowhere else.
/// </summary>
public enum AnswerType
{
    YesNo,
    SingleSelect,
    ShortText
}
