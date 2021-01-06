FROM mcr.microsoft.com/dotnet/core/sdk:3.1

ARG VERSION=master
ENV VERSION=${VERSION}

RUN git clone https://github.com/stratisproject/StratisFullNode.git -b "release/${VERSION}" && cd /StratisFullNode/src/Stratis.StraxD && dotnet build
VOLUME /root/.stratisfullnode
WORKDIR /StratisFullNode/src/Stratis.StraxD
EXPOSE 17105 17104 17103
CMD ["dotnet", "run"]