# WaveTech Fluxer Toolbox — C# bot (TTS needs ffmpeg in the runtime image)
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY WaveTechFluxerTTS.sln ./
COPY WaveTechFluxerTTS/ ./WaveTechFluxerTTS/

RUN dotnet restore WaveTechFluxerTTS/WaveTechFluxerTTS.csproj
RUN dotnet publish WaveTechFluxerTTS/WaveTechFluxerTTS.csproj -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/runtime:8.0 AS runtime
RUN apt-get update \
    && apt-get install -y --no-install-recommends ffmpeg \
    && rm -rf /var/lib/apt/lists/*

WORKDIR /app
COPY --from=build /app/publish ./

ENV Data__Root=/app/Data
VOLUME ["/app/Data"]

ENTRYPOINT ["dotnet", "WaveTechFluxerTTS.dll"]
