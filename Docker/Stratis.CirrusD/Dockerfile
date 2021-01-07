FROM mcr.microsoft.com/dotnet/core/sdk:3.1

ARG VERSION=master
ENV VERSION=${VERSION}

RUN if [ "$VERSION" = "master" ]; then git clone https://github.com/stratisproject/StratisFullNode.git; else git clone https://github.com/stratisproject/StratisFullNode.git -b "release/${VERSION}"; fi 
RUN cd /StratisFullNode/src/Stratis.StraxD && dotnet build
VOLUME /root/.stratisfullnode
WORKDIR /StratisFullNode/src/Stratis.CirrusD
EXPOSE 16179 16175 37223
CMD ["dotnet", "run"]