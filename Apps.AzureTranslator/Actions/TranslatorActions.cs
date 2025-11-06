using Apps.MicrosoftTranslator.Constants;
using Apps.MicrosoftTranslator.Invocables;
using Apps.MicrosoftTranslator.Model;
using Apps.MicrosoftTranslator.Model.Request;
using Apps.MicrosoftTranslator.Model.Response;
using Apps.MicrosoftTranslator.Utils;
using Azure.AI.Translation.Text;
using Blackbird.Applications.Sdk.Common;
using Blackbird.Applications.Sdk.Common.Actions;
using Blackbird.Applications.Sdk.Common.Invocation;
using Blackbird.Applications.SDK.Extensions.FileManagement.Interfaces;
using Blackbird.Applications.Sdk.Utils.Extensions.Files;
using Blackbird.Applications.Sdk.Utils.Extensions.Sdk;
using RestSharp;
using Blackbird.Applications.Sdk.Common.Exceptions;
using Newtonsoft.Json;
using Apps.MicrosoftTranslator.Model.Dtos;

namespace Apps.MicrosoftTranslator.Actions;

[ActionList("Translator")]
public class TranslatorActions(InvocationContext invocationContext, IFileManagementClient fileManagementClient)
    : AzureTextTranslatorInvocable(invocationContext)
{
    private static List<string> _supportedFileTypes =
    [
        ".txt", ".txv", ".tab", ".csv", ".html", ".htm", ".mthml", ".mht", ".pptx", ".xlsx", ".docx", ".msg", ".xlf", ".xliff"
    ];
    
    [Action("Translate", Description = "Translates the text to the target language.")]
    public async Task<TranslationResponse> Translate([ActionParameter] TextTranslationInput input)
    {
        ProfanityAction? profanityAction = input.ProfanityAction != null 
            ? Enum.Parse<ProfanityAction>(input.ProfanityAction, true) 
            : null;

        ProfanityMarker? profanityMarker = input.ProfanityMarker != null 
            ? Enum.Parse<ProfanityMarker>(input.ProfanityMarker, true) 
            : null;

        TextType? textType = input.TextType != null 
            ? new TextType(input.TextType) 
            : (TextType?)null;

        var response = await ExecuteWithErrorHandlingAsync(async () => await Client.TranslateAsync(new[] { input.TargetLanguage }, 
            new[] { input.Text }, 
            sourceLanguage: input.SourceLanguage, 
            textType: textType, 
            category: input.Category,
            profanityAction: profanityAction,
            profanityMarker: profanityMarker,
            includeAlignment: input.IncludeAlignment ?? false,
            includeSentenceLength: input.IncludeSentenceLength ?? false,
            suggestedFrom: input.SuggestedFrom,
            fromScript: input.FromScript,
            toScript: input.ToScript,
            allowFallback: input.AllowFallback ?? false
        ));

        return new(response);
    }

    [Action("Translate document", Description = "Translates the document to the target language. Under the hood we are using synchronous translation.")]
    public async Task<TranslateDocumentResponse> TranslateDocument([ActionParameter] FileModel file,
        [ActionParameter] TranslationInput input)
    {
        if (!_supportedFileTypes.Contains(file.File.GetFileExtension()))
        {
            throw new PluginMisconfigurationException($"File type {file.File.GetFileExtension()} is not supported for translation. " +
                                                $"Supported file types are: {string.Join(", ", _supportedFileTypes)}.");
        }
        
        var url = Creds.GetDocumentTranslationUrl();
        var key = Creds.Get(CredsNames.ApiKey).Value;
    
        var fileStream = await fileManagementClient.DownloadAsync(file.File);
        var bytes = await fileStream.GetByteData();
        var options = new RestClientOptions(url)
        {
            MaxTimeout = -1,
        };
        
        var client = new RestClient(options);
        
        var requestUrl = $"/translator/document:translate?targetLanguage={input.TargetLanguage}&api-version=2024-05-01";
        if (!string.IsNullOrEmpty(input.SourceLanguage))
        {
            requestUrl += $"&sourceLanguage={input.SourceLanguage}";
        }
        
        if (!string.IsNullOrEmpty(input.Category))
        {
            requestUrl += $"&category={input.Category}";
        }
        
        if (input.AllowFallback.HasValue)
        {
            requestUrl += $"&allowFallback={input.AllowFallback.Value}";
        }
        
        var request = new RestRequest(requestUrl, Method.Post);
        request.AddHeader("Ocp-Apim-Subscription-Key", key);
        request.AlwaysMultipartFormData = true;
        request.AddFile("document", bytes, file.File.Name, file.File.ContentType);
    
        var response = await ExecuteWithErrorHandlingAsync(async () => await client.ExecuteAsync(request));
        if (!response.IsSuccessful)
        {
            var error = JsonConvert.DeserializeObject<ErrorDto>(response.Content!)!;
            throw new PluginApplicationException(error.ToString());
        }
    
        var byteResponse = response.RawBytes!;
        var memoryStream = new MemoryStream(byteResponse);
        memoryStream.Position = 0;
        var fileReference = await fileManagementClient.UploadAsync(memoryStream, file.File.ContentType, file.File.Name);
        return new()
        {
            File = fileReference
        };
    }

    [Action("Transliterate", Description = "Transliterates the text to the target script.")]
    public async Task<TransliterationResponse> Transliterate([ActionParameter] TransliterationInput input)
    {
        var response =
            await ExecuteWithErrorHandlingAsync(async ()=> await Client.TransliterateAsync(input.SourceLanguage, input.FromScript, input.ToScript, input.Text));
        return new(response);
    }
}