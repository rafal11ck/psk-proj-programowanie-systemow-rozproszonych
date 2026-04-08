{
  inputs = {
    utils.url = "github:numtide/flake-utils";
  };
  outputs =
    {
      self,
      nixpkgs,
      utils,
    }:
    utils.lib.eachDefaultSystem (
      system:
      let
        pkgs = nixpkgs.legacyPackages.${system};
      in
      {
        devShells.default = pkgs.mkShell {

          buildInputs = with pkgs; [
            dotnet-sdk_10
            ncurses
            icu
          ];

          LD_LIBRARY_PATH = pkgs.lib.makeLibraryPath [
            pkgs.icu
            pkgs.ncurses
          ];
        };
      }
    );
}
