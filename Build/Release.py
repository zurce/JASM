
import os
import shutil
import re
import sys


JASM_CSPROJ = "src\\GIMI-ModManager.WinUI\\GIMI-ModManager.WinUI.csproj"
JASM_OUTPUT = "src\\GIMI-ModManager.WinUI\\bin\\Release\Publish\\"

RELEASE_DIR = "output"
JASM_RELEASE_DIR = "output\\JASM"

SelfContained =  sys.argv[1] == "SelfContained" if len(sys.argv) > 1  else False

def checkSuccessfulExitCode(exitCode: int) -> None:
	if exitCode != 0:
		print("Exit code: " + str(exitCode))
		exit(exitCode)

def extractVersionNumber() -> str:
	with open(JASM_CSPROJ, "r") as jasmCSPROJ:
		for line in jasmCSPROJ:
			line = line.strip()
			if line.startswith("<VersionPrefix>"):
				return re.findall("\d+\.\d+\.\d+", line)

print("PostBuild.py")
print("PWD: " + os.getcwd())
print("SelfContained: " + str(SelfContained))

versionNumber = extractVersionNumber()
if versionNumber is None or len(versionNumber) == 0:
	print("Failed to extract version number from " + JASM_CSPROJ)
	exit(1)
versionNumber = versionNumber[0]

print("Building JASM+...")
jasmPublishCommand = "dotnet publish " + JASM_CSPROJ + (" /p:PublishProfile=FolderProfileSelfContained.pubxml" if SelfContained else " /p:PublishProfile=FolderProfile.pubxml") + " -c Release" 
print(jasmPublishCommand)
checkSuccessfulExitCode(os.system(jasmPublishCommand))
print()
print("Finished building JASM+")

# Create release directory
os.makedirs(RELEASE_DIR, exist_ok=True)
os.makedirs(JASM_RELEASE_DIR, exist_ok=True)

print("Copying text files to RELEASE_DIR...")
shutil.copy("Build\\README.txt", RELEASE_DIR)
shutil.copy("CHANGELOG.md", RELEASE_DIR + "\\CHANGELOG.txt")

print("Finished copying text files to release directory")

print("Zipping release directory...")
releaseArchiveName = "JASM_v" + versionNumber + ".zip"
if (SelfContained):
	releaseArchiveName = "SelfContained_" + releaseArchiveName

checkSuccessfulExitCode(os.system(f"7z a -tzip -mx4 {releaseArchiveName} .\\{RELEASE_DIR}\\*"))
print()
print("Finished zipping release directory")

env_file = os.getenv('GITHUB_ENV')
if env_file is None:
	exit(1)

with open(env_file, "a") as myfile:
    myfile.write(f"zipFile={releaseArchiveName}")

checkSuccessfulExitCode(os.system(f"7z h -scrcsha256 .\\{releaseArchiveName}"))


exit(0)



