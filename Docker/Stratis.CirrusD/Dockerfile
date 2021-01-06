FROM mcr.microsoft.com/dotnet/core/sdk:3.1

ARG VERSION=master
ENV VERSION=${VERSION}

RUN git clone https://github.com/stratisproject/StratisFullNode.git -b "release/${VERSION}" && cd /StratisFullNode/src/Stratis.CirrusD && dotnet build
VOLUME /root/.stratisfullnode
WORKDIR /StratisFullNode/src/Stratis.CirrusD
EXPOSE 16179 16175 37223
CMD ["dotnet", "run"]