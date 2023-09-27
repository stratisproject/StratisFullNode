FROM mcr.microsoft.com/dotnet/sdk:6.0 AS build

ARG APPVERSION=master
ENV APPVERSION=${APPVERSION}

RUN if [ "$APPVERSION" = "master" ]; then git clone https://github.com/stratisproject/StratisFullNode.git; else git clone https://github.com/stratisproject/StratisFullNode.git -b "release/${APPVERSION}"; fi 
RUN cd /StratisFullNode/src/Stratis.StraxD && dotnet build

FROM mcr.microsoft.com/dotnet/aspnet:6.0

COPY --from=build /StratisFullNode/src/Stratis.StraxD/bin/Debug/net6.0 .

VOLUME /root/.stratisnode

WORKDIR .

EXPOSE 17102 17103 17104 17105

ENTRYPOINT ["dotnet", "Stratis.StraxD.dll"]