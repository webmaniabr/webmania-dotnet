FROM mcr.microsoft.com/dotnet/sdk@sha256:afd0ccff9d306812a69ef0f47458376b6acb544ae866cfc4b8d28ff0eef35ea7
WORKDIR /work
COPY . .
ENV DOTNET_CLI_TELEMETRY_OPTOUT=1
CMD ["scripts/verify.sh", "/usr/share/dotnet/dotnet"]
