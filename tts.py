import pyttsx3
import sys

filename = sys.argv[1]
text = sys.argv[2]

engine = pyttsx3.init()
engine.save_to_file(text, filename)
engine.runAndWait()