using Azure;
using Azure.AI.OpenAI;
using Azure.Identity;
using Azure.Search.Documents.Indexes;
using Azure.Search.Documents.Indexes.Models;
using DotNetEnv;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.VectorData;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.AzureAISearch;
using Microsoft.SemanticKernel.Connectors.AzureOpenAI;
using Microsoft.SemanticKernel.Connectors.InMemory;
using Microsoft.SemanticKernel.Embeddings;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

// Program class
class Program
{
    // Change to constant
    private const int model_embeddings_dimension = 3072; //text-embedding-3-large

    // Main method
    static async Task Main(string[] args)
    {
        try
        {
            // Load the .env file
            Env.Load();

            // Load Endpoints from config file
            string aoai_endpoint = Environment.GetEnvironmentVariable("AZURE_OPENAI_ENDPOINT");
            string aisearch_endpoint = Environment.GetEnvironmentVariable("AISEARCH_ENDPOINT");

            // Load Model Deployment name from config file
            // string model_chat_deployment_name = Environment.GetEnvironmentVariable("MODEL_CHAT_DEPLOYMENT_NAME");
            string model_embeddings_deployment_name = Environment.GetEnvironmentVariable("MODEL_EMBEDDINGS_DEPLOYMENT_NAME");

            // Load Model Id from config file
            // string model_chat_id = Environment.GetEnvironmentVariable("MODEL_CHAT_ID");
            string model_embeddings_id = Environment.GetEnvironmentVariable("MODEL_EMBEDDINGS_ID");

            // Specify the model version
            // string model_chat_version = Environment.GetEnvironmentVariable("MODEL_CHAT_VERSION");
            string model_embeddings_version = Environment.GetEnvironmentVariable("MODEL_EMBEDDINGS_VERSION");

            // Load Credentials
            var credentials = new DefaultAzureCredential();

            // Create a search index client.
            var indexClient = new SearchIndexClient(new Uri(aisearch_endpoint), credentials);
            
            // Construct an InMemory vector store.
            // var vectorStore = new InMemoryVectorStore();

            // Create a vector store using Azure AI Search.
            var vectorStore = new AzureAISearchVectorStore(
                indexClient);

            // Create an embedding generation service.
            var textEmbeddingGenerationService = new AzureOpenAITextEmbeddingGenerationService(
                    model_embeddings_deployment_name,
                    aoai_endpoint,
                    credentials,
                    apiVersion: model_embeddings_version);

            // Get and create collection if it doesn't exist.
            var collection = vectorStore.GetCollection<string, Glossary>("skglossary");
            await collection.CreateCollectionIfNotExistsAsync();

            // Create glossary entries and generate embeddings for them.
            var glossaryEntries = CreateGlossaryEntries().ToList();
            var tasks = glossaryEntries.Select(entry => Task.Run(async () =>
            {
                entry.DefinitionEmbedding = new ReadOnlyMemory<float>((await textEmbeddingGenerationService.GenerateEmbeddingAsync(entry.Definition).ConfigureAwait(false)).ToArray());
                Console.WriteLine($"entry.DefinitionEmbedding: '{entry.DefinitionEmbedding}'");
            }));
            await Task.WhenAll(tasks);

            // Upsert the glossary entries into the collection and return their keys.
            var upsertedKeysTasks = glossaryEntries.Select(async x =>
            {
                try
                {
                    // Delete the entry if it already exists.
                    await collection.DeleteAsync(x.Key);
                    // Upsert the entry.
                    return await collection.UpsertAsync(x);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error upserting entry '{x.Key}': {ex.Message}\n{ex.StackTrace}");
                    throw;
                }
            });
            var upsertedKeys = await Task.WhenAll(upsertedKeysTasks);

            // Search the collection using a vector search.
            var searchString = "What is an Application Programming Interface";
            var searchVector = await textEmbeddingGenerationService.GenerateEmbeddingAsync(searchString);
            var searchResult = await collection.VectorizedSearchAsync(searchVector, new() { Top = 1 });
            var resultRecords = new List<VectorSearchResult<Glossary>>();
            await foreach (var result in searchResult.Results)
            {
                resultRecords.Add(result);
            }

            Console.WriteLine("Search string: " + searchString);
            Console.WriteLine("Result: " + resultRecords.First().Record.Definition);
            Console.WriteLine();

            // Search the collection using a vector search.
            searchString = "What is Retrieval Augmented Generation";
            searchVector = await textEmbeddingGenerationService.GenerateEmbeddingAsync(searchString);
            searchResult = await collection.VectorizedSearchAsync(searchVector, new() { Top = 1 });
            resultRecords = new List<VectorSearchResult<Glossary>>();
            await foreach (var result in searchResult.Results)
            {
                resultRecords.Add(result);
            }

            Console.WriteLine("Search string: " + searchString);
            Console.WriteLine("Result: " + resultRecords.First().Record.Definition);
            Console.WriteLine();

            // Search the collection using a vector search with pre-filtering.
            searchString = "What is Retrieval Augmented Generation";
            searchVector = await textEmbeddingGenerationService.GenerateEmbeddingAsync(searchString);
            var filter = new VectorSearchFilter().EqualTo(nameof(Glossary.Category), "External Definitions");
            searchResult = await collection.VectorizedSearchAsync(searchVector, new() { Top = 3, Filter = filter });
            resultRecords = new List<VectorSearchResult<Glossary>>();
            await foreach (var result in searchResult.Results)
            {
                resultRecords.Add(result);
            }

            Console.WriteLine("Search string: " + searchString);
            Console.WriteLine("Number of results: " + resultRecords.Count);
            Console.WriteLine("Result 1 Score: " + resultRecords[0].Score);
            Console.WriteLine("Result 1: " + resultRecords[0].Record.Definition);
            Console.WriteLine("Result 2 Score: " + resultRecords[1].Score);
            Console.WriteLine("Result 2: " + resultRecords[1].Record.Definition);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"An error occurred: {ex.Message}");
            Console.WriteLine($"Stack Trace: {ex.StackTrace}");
        }
    }

    /// <summary>
    /// Sample model class that represents a glossary entry.
    /// </summary>
    /// <remarks>
    /// Note that each property is decorated with an attribute that specifies how the property should be treated by the vector store.
    /// This allows us to create a collection in the vector store and upsert and retrieve instances of this class without any further configuration.
    /// </remarks>
    private sealed class Glossary
    {
        [VectorStoreRecordKey]
        public string Key { get; set; }

        [VectorStoreRecordData(IsFilterable = true)]
        public string Category { get; set; }

        [VectorStoreRecordData]
        public string Term { get; set; }

        [VectorStoreRecordData]
        public string Definition { get; set; }

        [VectorStoreRecordVector(model_embeddings_dimension)]
        public ReadOnlyMemory<float> DefinitionEmbedding { get; set; }
    }

    /// <summary>
    /// Create some sample glossary entries.
    /// </summary>
    /// <returns>A list of sample glossary entries.</returns>
    private static IEnumerable<Glossary> CreateGlossaryEntries()
    {
        yield return new Glossary
        {
            Key = "1",
            Category = "External Definitions",
            Term = "API",
            Definition = "Application Programming Interface. A set of rules and specifications that allow software components to communicate and exchange data."
        };

        yield return new Glossary
        {
            Key = "2",
            Category = "Core Definitions",
            Term = "Connectors",
            Definition = "Connectors allow you to integrate with various services provide AI capabilities, including LLM, AudioToText, TextToAudio, Embedding generation, etc."
        };

        yield return new Glossary
        {
            Key = "3",
            Category = "External Definitions",
            Term = "RAG",
            Definition = "Retrieval Augmented Generation - a term that refers to the process of retrieving additional data to provide as context to an LLM to use when generating a response (completion) to a user’s question (prompt)."
        };
    }
}