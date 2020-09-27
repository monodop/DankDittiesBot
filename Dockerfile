# DOTNET CORE
FROM mcr.microsoft.com/dotnet/core/sdk:3.1 AS clientBuilder
WORKDIR /app

COPY src/*.sln .
COPY src/DankDitties/*.csproj ./DankDitties/
RUN dotnet restore

COPY src/ .
RUN dotnet publish -c Release -o dist

# PYTHON
FROM amd64/debian:buster-slim as pythonBuilder
RUN apt-get update \
    && apt-get install -y --no-install-recommends \
        python3 \
        python3-pip \
        build-essential \
        gcc \
    && pip3 install setuptools wheel

COPY requirements.txt .
RUN pip3 install --user -r requirements.txt

# FINAL IMAGE
FROM mcr.microsoft.com/dotnet/core/runtime:3.1
WORKDIR /app

RUN echo "deb http://deb.debian.org/debian buster non-free" >> /etc/apt/sources.list
RUN apt-get update \
    && apt-get install -y --no-install-recommends \
        ffmpeg \
        libsodium-dev \
        libopus-dev \
        espeak \
        libespeak1 \
        ca-certificates \
        libttspico-utils \
        python3

COPY --from=clientBuilder /app/dist /app/client
COPY src/DankDitties/dependencies/linux /app/client

COPY --from=pythonBuilder /root/.local /root/.local
COPY *.py /app/python/
ENV PATH=/root/.local/bin:$PATH
ENV PYTHON_EXE=python3
ENV SCRIPT_DIR=/app/python
ENV DATA_DIR=/data

CMD ["dotnet", "/app/client/DankDitties.dll", "/app"]
