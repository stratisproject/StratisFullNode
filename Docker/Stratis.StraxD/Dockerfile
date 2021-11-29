FROM mcr.microsoft.com/dotnet/core/sdk:3.1

ARG APPVERSION=master
ENV APPVERSION=${APPVERSION}

RUN if [ "$APPVERSION" = "master" ]; then git clone https://github.com/stratisproject/StratisFullNode.git; else git clone https://github.com/stratisproject/StratisFullNode.git -b "release/${APPVERSION}"; fi 
RUN cd /StratisFullNode/src/Stratis.StraxD && dotnet build
VOLUME /root/.stratisfullnode
WORKDIR /StratisFullNode/src/Stratis.StraxD
EXPOSE 17105 17104 17103
ENTRYPOINT ["dotnet", "run"]