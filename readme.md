# Overview
This is a simple project to demonstrate the use of Managed Identity and AISearch.

# Instructions

Deploy infra using the following commands:
```bash
azd auth login
azd up
```

Copy sample.env to .env.
Fill in the following
- AZURE_OPENAI_ENDPOINT=
- AISEARCH_ENDPOINT=
- AISEARCH_INDEXNAME=
- MODEL_CHAT_DEPLOYMENT_NAME=
- MODEL_CHAT_ID=
- MODEL_CHAT_VERSION=
- MODEL_EMBEDDINGS_DEPLOYMENT_NAME=
- MODEL_EMBEDDINGS_ID=
- MODEL_EMBEDDINGS_VERSION=
- MODEL_EMBEDDING_DIMENSIONS=

Run the project using the following command(s):

```
dotnet run --project ./Project.csproj
```