FROM mcr.microsoft.com/dotnet/sdk:3.1 AS build

ARG APPVERSION=master
ENV APPVERSION=${APPVERSION}

RUN if [ "$APPVERSION" = "master" ]; then git clone https://github.com/stratisproject/StratisFullNode.git; else git clone https://github.com/stratisproject/StratisFullNode.git -b "release/${APPVERSION}"; fi 
RUN cd /StratisFullNode/src/Stratis.StraxD && dotnet build

FROM mcr.microsoft.com/dotnet/aspnet:3.1

COPY --from=build /StratisFullNode/src/Stratis.StraxD/bin/Debug/netcoreapp3.1 .

VOLUME /root/.stratisnode

WORKDIR .

EXPOSE 17102 17103 17104 17105

ENTRYPOINT ["dotnet", "Stratis.StraxD.dll"]