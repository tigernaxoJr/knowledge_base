namespace Assistant.Core.Prompts;

public interface IPromptProvider
{
    string TitleGeneration { get; }
    string MultiDocumentMerge { get; }
    string MultiDocumentTitleGeneration { get; }
    string KnowledgeEntryMerge { get; }
    string MultiDocumentMergeAndTitle { get; }
}
