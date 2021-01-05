FROM mcr.microsoft.com/dotnet/core/sdk:3.1

ARG VERSION=master
ENV VERSION=${VERSION}

RUN git clone https://github.com/stratisproject/StratisFullNode.git -b "release/${VERSION}" && cd /StratisFullNode/src/Stratis.StraxD && dotnet build
VOLUME /root/.stratisfullnode
WORKDIR /StratisFullNode/src/Stratis.StraxD
EXPOSE 27103 27104 27105
CMD ["dotnet", "run", "-testnet"]