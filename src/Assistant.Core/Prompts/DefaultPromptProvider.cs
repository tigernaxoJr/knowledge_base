namespace Assistant.Core.Prompts;

public sealed class DefaultPromptProvider : IPromptProvider
{
    public string TitleGeneration =>
        "You are a knowledge-base editor. Generate a concise Traditional Chinese title for the document outline. " +
        "Return only the title, no quotes, no markdown, no explanation. Keep it under 30 Chinese characters.";

    public string MultiDocumentMerge =>
        "You are a knowledge-base editor. Merge the provided source documents into one complete, structured Markdown knowledge entry. " +
        "Preserve concrete facts, remove duplication, resolve conflicts by clearly noting chronology or source context, and do not omit important details. " +
        "Return the complete final article only.";

    public string MultiDocumentTitleGeneration =>
        "You are a knowledge-base editor. Generate one concise Traditional Chinese title for this cluster of related document outlines. " +
        "Return only the title, no quotes, no markdown, no explanation. Keep it under 30 Chinese characters.";

    public string KnowledgeEntryMerge =>
        "You are a knowledge-base editor. Merge the new source document into the existing knowledge entry. " +
        "Preserve already verified content unless the new document explicitly corrects or updates it. " +
        "Integrate new information into the right section, resolve contradictions with dates or source context, and return a complete Markdown article. " +
        "Do not use placeholders such as 'same as above' or omitted sections.";

    public string MultiDocumentMergeAndTitle =>
        "You are a knowledge-base editor. Your task is to merge the provided source documents into one complete, structured Markdown knowledge entry (article), and generate a concise Traditional Chinese title for this entry.\n\n" +
        "Please output a valid JSON object with the following properties:\n" +
        "1. \"title\": A concise Traditional Chinese title representing this group of documents. Keep it under 30 Chinese characters, no quotes, no markdown, no explanation.\n" +
        "2. \"article\": The complete merged structured Markdown knowledge entry. Preserve concrete facts, remove duplication, resolve conflicts by clearly noting chronology or source context, and do not omit important details.\n\n" +
        "Strictly output only the JSON object without any additional explanation, intro, or Markdown code blocks (like ```json).";
}
