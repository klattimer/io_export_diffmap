using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using System.Text.RegularExpressions;

public class MetaMorphSettings {
	public GameObject meshObject;				// Object to Morph
	public bool isBoned = true;					//
	public bool UVLayerOne = true;				// Use first or second UV layer
	public bool recalculateNormals = false;		// Run mesh.RecalculateNormals() every frame
	public bool onlyWhenVisible = true;			// On animate visible mesh
	public int verbose = 0;
}

public class dataTemplate {
	public string name;
}

public class DiffMap : dataTemplate {
	public Texture2D image;
	public float multiplier = 0;
	public Vector3 scale;

	DiffMap () {
		scale = new Vector3(1,1,1);
	}
}

public class MorphItem {
	public Vector3[] ShapeMorph;
	public int[] ShapeLink;
	public float ShapePower;
}

public class AnimationRecording : dataTemplate {
	//public string name;							// ID for a shapekey animation set
	public TextAsset text;						// Textfile with the animation data
}

public class CurrentlyActiveMorph : dataTemplate {
	//string name;  // relates directly to Diff_Map_class.AR_Name
	public int link; // link to array Diff_Map_class[] (speed up loops)
	
	public float CAMStartTime;
	public float CAMTimeFrame; 
	
	public float CAMStartLevel; // The time of the animation being started, using Time.realtimeSinceStartup
	public float CAMEndLevel;
	
	public bool CAMEnd;
}

public class CurrentlyActiveAnimation : dataTemplate {
	//string name;  // relates directly to Animation_Recording_class.AR_Name
	public int link; // link to array Animation_Recording_class[] (speed up loops)
	
	public float CAAStartTime; // The time of the animation being started, using Time.realtimeSinceStartup
	public float CAAEndTime; 
	public float CAAFadeTime; 
	
	public float CAASpeed;
	
	public float CAAEffectLevel; 
	
	public int CAAStyle; 
}

public class AnimationStyles {
	public const int end = 0;
	public const int freeze = 1;
	public const int loop = 2;
	public const int pingPong = 3;
}

public class MetaMorph : MonoBehaviour {
	public MetaMorphSettings metaMorphSettings;
	public DiffMap[] diffMaps;
	public AnimationRecording[] animationRecordings;

	private bool isVisible = false;
	private bool isReady = false;
	private int meshChanged = 0;

	private Mesh mesh;
	private Vector3[] baseMesh;
	private Vector3[] modMesh;

	private List<int> seamVerts;
	private List<int> seamNextSameVert;

	private List<List<Vector3>> morphShapesData;
	private List<List<int>> morphShapesLinks;
	private List<List<List<string>>> morphAnimationData;
	//private List<> playingAnimations;

	private List<CurrentlyActiveMorph> currentlyActiveMorphs;
	private List<CurrentlyActiveAnimation> currentlyActiveAnimations;

	void LoadMeshData() {
		Report("Loading Mesh Data", 1);
		// setup mesh based on type of object.
		if (metaMorphSettings.isBoned == true) {
			MeshFilter f = (MeshFilter) metaMorphSettings.meshObject.GetComponent("SkinnedMeshRenderer");
			mesh = f.sharedMesh;
		} else {
			MeshFilter f = (MeshFilter) metaMorphSettings.meshObject.GetComponent("MeshFilter");
			mesh = f.mesh ;
		}
		
		baseMesh = mesh.vertices; // This data is for restoring the original mesh.
		modMesh = mesh.vertices;  // And this is for modding.
	}

	void LoadMeshSeams() {
		Report("Loading Mesh Seams", 1);
		// This function finds inital overlapping verts and treats them so they ALWAYS overlap.
		// This means your mesh should never have overlapping verts that are NOT along a UV seam.

		seamNextSameVert = new List<int>();
		seamVerts = new List<int>();

		// We're going to go though every vert, and see if any other verts are 
		for ( int vert = 0 ; vert < baseMesh.Length ; vert++ ) {
			seamNextSameVert.Add(  -1 );
			
			for ( int findvert = vert + 1 ; findvert < baseMesh.Length ; findvert++ ) {
				if (baseMesh[vert] == baseMesh[findvert]) {
					seamNextSameVert[vert] = findvert;
					seamVerts.Add(vert);
					findvert = baseMesh.Length;
				}
			}
		}
		Report("Found " + seamVerts.Count + " seam verts...",1);
	}

	void LoadDiffMaps() {
		Report("Loading Diff Maps", 1);
		morphShapesData = new List<List<Vector3>>();
		morphShapesLinks = new List<List<int>> ();
		// First, get the UV data from the mesh...
		Vector2[] baseUVs;	
		if (metaMorphSettings.UVLayerOne == true) {
			baseUVs = mesh.uv;
		} else {
			baseUVs = mesh.uv2;
		}
		
		// let's cycle through all the diff maps in Diff_Maps.
		int diffMapLoopMax = diffMaps.Length;	
		for ( int diffMapLoop = 0 ; diffMapLoop < diffMapLoopMax ; diffMapLoop++ )
		{
			// Temporary variables for building 
			List<Vector3> loadDiffMap = new List<Vector3> ();   
			List<int> loadDiffMapL = new List<int> ();
			
			if (diffMaps[diffMapLoop].scale == Vector3.zero) {
				diffMaps[diffMapLoop].scale = new Vector3(1,1,1);
			}
			
			// Set the name if it was not alreay set...
			if (diffMaps[diffMapLoop].name == "") {
				diffMaps[diffMapLoop].name = diffMaps[diffMapLoop].image.name;
			}
			
			// Grab the Diff Map data...
			DiffMap aDiffMap = diffMaps[diffMapLoop];
			
			// And get the parts of it we need for processing...
			Texture2D diffMapImage = aDiffMap.image; 
			Vector3 diffMapScale = aDiffMap.scale;
			
			// We now read the mesh, uv, and Diff Map to get the shape data. and store it.
			for (int vert = 0 ; vert < baseUVs.Length ; vert++) {
				int UV_x = (int)Mathf.Round(baseUVs[vert].x * diffMapImage.width);
				int UV_y = (int)Mathf.Round(baseUVs[vert].y * diffMapImage.height);

				// These 
				int test_x = (int)Mathf.Round((diffMapImage.GetPixel(UV_x, UV_y).r - 0.5f) * 255);
				int test_y = (int)Mathf.Round((diffMapImage.GetPixel(UV_x, UV_y).g - 0.5f) * 255);
				int test_z = (int)Mathf.Round((diffMapImage.GetPixel(UV_x, UV_y).b - 0.5f) * 255);
				
				if ( !(test_x == 0 && test_y == 0 && test_z == 0) ) {
					// Okay, now we grab the color data for the pixel under the UV point for this vert.  We then convert it to a number from -1.0 to 1.0, and multiply it by Diff_Map_Scale.
					float UVC_r = ((diffMapImage.GetPixel(UV_x, UV_y).r / 0.5f)  - 1 ) * -1 * diffMapScale.x; // Why -1?  Because the relation to blender is reversed for some reason...
					float UVC_g = ((diffMapImage.GetPixel(UV_x, UV_y).g / 0.5f)  - 1 ) * diffMapScale.y;
					float UVC_b = ((diffMapImage.GetPixel(UV_x, UV_y).b / 0.5f)  - 1 ) * diffMapScale.z;
					
					Vector3 vert_xyz_shift = new Vector3 (UVC_r, UVC_g, UVC_b);

					loadDiffMap.Add(vert_xyz_shift);
					loadDiffMapL.Add(vert);
				}
			}
			Report("Object "+name+": Diff Map '"+diffMapImage.name+"' changes " + loadDiffMapL.Count + " out of " + baseMesh.Length + " verts...", 2);
			
			// This part stores not only the mesh modifications for this shape, but also the indexes of the array that are being changes in the mesh.
			// This data allows us to cycle through only the mesh points that will be changed, instead of running though all of points of the mesh. 
			// Massive speed increase:  You only pay for the points you morph.
			
			morphShapesData.Add (loadDiffMap); // We're storing an array into an array here.
			morphShapesLinks.Add (loadDiffMapL); // We're storing an array into an array here.
		}
	}

	void LoadAnimationRecordings() {
		Report("Loading Animation Recordings", 1);
		morphAnimationData = new List<List<List<string>>> ();
		for (int fileloop = 0; fileloop <  animationRecordings.Length; fileloop++) {
			AnimationRecording ar = animationRecordings[fileloop];
			
			List<List<string>> animationArray = ReadAnimationFile(ar.text);
			
			// Okay, we have the data for this animation kit...
			// Let's store it!
			morphAnimationData.Add(animationArray);
			// Now it's stored in the format: Morph_Sequence_Data[animationset][frame1-xxx][name/amountmorph]
		}
	}
	
	List<List<string>> ReadAnimationFile(TextAsset datafile) {
		// This read blender 3d ShapeKeys that have been exported with the diffmap.
		// It's a good idea for each animation to contained in it's own file under it's own name.
		Report("Loading Animation Recording: " + datafile.name, 2);
		List<List<string>> animationArray = new List<List<string>>();
		//var Total_String = datafile.text;
		
		string[] Total_Array = datafile.text.Split("\n"[0]);

		for (int line = 0 ; line < Total_Array.Length ; line=line+1) {
			string lineString = Total_Array[line];

			// parse out all the crap.
			Match boo = Regex.Match(lineString,"(\\[|\\])");
			if (boo.Success) {
				lineString = Regex.Replace(lineString,"(\n|\r|\f)","");
				lineString = Regex.Replace(lineString,"(\\[|\\])","");
				lineString = Regex.Replace(lineString,"\\s*(,)\\s*","|");
				lineString = Regex.Replace(lineString,"'","");
				
				string[] lineArray = lineString.Split("|"[0]);
				
				int item;
				
				// We really want the floating point numbers to be stored as floating points, and not strings...
				if (animationArray.Count == 0) {
					animationArray.Add(arrayToList(lineArray));
					
					List<string> lineArray2 = new List<string>();
					for (item = 0 ; item < animationArray[0].Count ; item=item+1) {
						int Found = FindName( diffMaps, animationArray[0][item] );
						if ( Found != -1) {
							lineArray2.Add(Found.ToString()); // Writing line two, the diff map indexes.  Faster than name lookups per frame.
						} else {
							Report("ERROR: Morph not found" + animationArray[0][item], 0);
							Debug.Break();
						}
					}
					animationArray.Add(lineArray2);
					
				} else {
					for (item = 0 ; item < lineArray.Length ; item=item+1) {
						int Found2 = FindName( diffMaps, animationArray[0][item] );
						if ( Found2 != -1) {
							if (animationArray.Count == 2 && diffMaps[Found2].multiplier == 0) {
								diffMaps[Found2].multiplier = float.Parse(lineArray[item]);
							}
						}
					}
					animationArray.Add(arrayToList(lineArray));
				}
			}
		}
		
		return animationArray;
	}

	void InitMetaMorph () {
		metaMorphSettings = new MetaMorphSettings ();

		currentlyActiveMorphs = new List<CurrentlyActiveMorph> ();
		currentlyActiveAnimations = new List<CurrentlyActiveAnimation> ();
		LoadMeshData ();
		LoadMeshSeams ();
		LoadDiffMaps ();
		LoadAnimationRecordings ();
		isReady = true;

		if (((Renderer)metaMorphSettings.meshObject.GetComponent ("Renderer")).isVisible || 
		    metaMorphSettings.onlyWhenVisible) {
			isVisible = true;
		}
	}

	// Use this for initialization
	void Start () {
		InitMetaMorph ();
	}

	// We need a Mesh render for this to work.  You may need to either place this script 
	// in the mesh itself, or place a mesh renderer component into the object this script 
	// is in. If not, then turn off metaMorphSettings.onlyWhenVisible.
	void OnBecameInvisible() {
		if (metaMorphSettings.onlyWhenVisible) {
			isVisible = false;
		}
	}

	// We need a Mesh render for this to work.  You may need to either place this script 
	// in the mesh itself, or place a mesh renderer component into the object this script 
	// is in. If not, then turn off etaMorphSettings.onlyWhenVisible.
	void OnBecameVisible() {
		isVisible = true;
	}

	void OnDisable() {
		modMesh = baseMesh;
		mesh.vertices = baseMesh;
		RecalculateNormals ();
	}
	
	// Update is called once per frame
	void Update () {
		// Process all Morph animations...
		// Note this neat trick where we only process animations every other frame.  
		// For facial morphs, this can be a serious saver
		// Remove it if you want...
		if (Time.frameCount % 2 == 0)
			ApplyMorphs();
	}

	void ApplyMorphs () {
		// This is where the magic happens.  All of the morphs and animations ar added together, and then applied to the mesh.
		List<MorphItem> morphArray = new List<MorphItem> ();
		
		morphArray = GroupMorphs(morphArray);
		morphArray = GroupAnimations(morphArray);
		
		// And if the mesh has changed since the last frame, we apply it.	
		if ( meshChanged > 0 && isVisible == true ) {
			// We have mesh changes!
			Vector3[] workMesh = modMesh;
			
			for (var itemLoop = 0 ; itemLoop < morphArray.Count ; itemLoop++ ) {
				// okay, we are now going to apply each animation at the proper precentage to the model.
				Vector3[] shapeMorph = morphArray[itemLoop].ShapeMorph;
				int[] shapeLink = morphArray[itemLoop].ShapeLink;
				float shapePower = morphArray[itemLoop].ShapePower;
				
				for (var verts = 0;verts < shapeLink.Length; verts++) {
					// In this case, we're only looping the vertices that MOVE.  All the rest are ignored, and this runs faster that way. 
					// You only pay for the parts you morph.
					workMesh[shapeLink[verts]] += shapeMorph[verts] * shapePower;
					
				}
			}
			
			// but you know what?  We have verts that need to be stiched back together along the UV seams.
			// Actually, we just re-overlap any overlapping vert positions from the initial mesh shape, but that's good enough!
			int[] seamVertsBIA = listToArray(seamVerts);
			int[] nextSameVertBIA = listToArray(seamNextSameVert);
			
			for ( var h=0 ; h<seamVertsBIA.Length ; h++ ) {
				workMesh[nextSameVertBIA[seamVertsBIA[h]]] = workMesh[seamVertsBIA[h]];
			}
			
			mesh.vertices = workMesh;
			
			// And recald the normals if needed.
			// Trust me, you want to leave this off.  It rarely works well.
			// Try it and see!
			RecalculateNormals();
			
			// And the mesh is ready for the next frame.
			meshChanged = meshChanged - 1;
			if (meshChanged < 0) {
				meshChanged = 0;
			}
		}
	}


	List<MorphItem> GroupMorphs (List<MorphItem> morphArray) {
		// Here is where we make the list of morphs to apply to the mesh from the Morphs currently active.
		for(int CAMLoop = 0 ; CAMLoop < currentlyActiveMorphs.Count ; CAMLoop++) {
			int morphNumber = currentlyActiveMorphs[CAMLoop].link;
			
			// How much should we morph it?
			float timeSpot = Mathf.InverseLerp(currentlyActiveMorphs[CAMLoop].CAMStartTime, 
			                                   currentlyActiveMorphs[CAMLoop].CAMStartTime + currentlyActiveMorphs[CAMLoop].CAMTimeFrame, 
			                                   Time.realtimeSinceStartup);
			var morphShapesPower = Mathf.Lerp(currentlyActiveMorphs[CAMLoop].CAMStartLevel, 
			                                  currentlyActiveMorphs[CAMLoop].CAMEndLevel, 
			                                  timeSpot);
			
			if (Mathf.Approximately( morphShapesPower, 0.0f)) {
				if (currentlyActiveMorphs[CAMLoop].CAMEnd == true) {
					currentlyActiveMorphs.RemoveAt(CAMLoop);  	// Watch this!  Make sure the for loop is actually looking at the length attribute or you could wander straight into null territory.
					CAMLoop--; 													// Oh, and back it up one, we just deleted an entry, so we need to look at this slot number again.  
					meshChanged = 2;
				}			
			} else {
				morphShapesPower = morphShapesPower * diffMaps[morphNumber].multiplier;
				// group up the data for the morph into an morph item array.
				MorphItem morphItem = new MorphItem ();
				morphItem.ShapeMorph = listToArray(morphShapesData[morphNumber]);	// section 0
				morphItem.ShapeLink = listToArray(morphShapesLinks[morphNumber]);	// section 1
				morphItem.ShapePower = morphShapesPower;				// section 2
				
				morphArray.Add(morphItem);
				meshChanged = 2;
			}
		}
		return morphArray;
	}
	
	List<MorphItem> GroupAnimations (List<MorphItem> morphArray) {
		// Here is where we make the list of morphs to apply to the mesh from the Animations currently active.
		for (int CAALoop = 0; CAALoop < currentlyActiveAnimations.Count; CAALoop++) {
			bool removeanimation = false;
			int animIndex = currentlyActiveAnimations[CAALoop].link;
			
			int Frame = Time2Frame( Time.realtimeSinceStartup - currentlyActiveAnimations[CAALoop].CAAStartTime, currentlyActiveAnimations[CAALoop].CAASpeed);
			int endFrame = morphAnimationData[animIndex].Count - 4;
			
			// Remember Animation styles?  This is where we process them.
			if (Frame > endFrame ) {
				int Style = currentlyActiveAnimations[CAALoop].CAAStyle;
				if (Style == AnimationStyles.end) {
					currentlyActiveAnimations[CAALoop].CAAEndTime = Time.realtimeSinceStartup - 0.002f;
					currentlyActiveAnimations[CAALoop].CAAFadeTime = Time.realtimeSinceStartup - 0.001f;
					Frame = endFrame;
				} else if (Style == AnimationStyles.freeze) {
					Frame = endFrame;
				} else if (Style == AnimationStyles.loop) {
					Frame = 0;
					currentlyActiveAnimations[CAALoop].CAAStartTime = Time.realtimeSinceStartup;
				} else if (Style == AnimationStyles.pingPong) {
					if (Frame > endFrame * 2) {
						Frame = 0;
						currentlyActiveAnimations[CAALoop].CAAStartTime = Time.realtimeSinceStartup;
					}
					Frame = (int)Mathf.Round(Mathf.PingPong(Frame, endFrame));
				}
			}
			
			// And the code for stopping an animatio and fading out over time while doing so.
			float fadeOut = 1.0f;
			if (currentlyActiveAnimations[CAALoop].CAAEndTime > 0) {
				fadeOut = Mathf.InverseLerp (currentlyActiveAnimations[CAALoop].CAAFadeTime, currentlyActiveAnimations[CAALoop].CAAEndTime, Time.realtimeSinceStartup);
				if(Time.realtimeSinceStartup > currentlyActiveAnimations[CAALoop].CAAFadeTime) {
					removeanimation = true;
				}
			}
			
			// Grabbing the data for adding to the morph list...
			List<string> morphIndexes  = morphAnimationData[animIndex][1]; // Indexes of the Diffmaps we're using.
			List<string> morphPowers = morphAnimationData[animIndex][2]; // frames start on line 3 (from line 0)...
			List<string> morphLevels = morphAnimationData[animIndex][Frame+3]; // frames start on line 3 (from line 0)...
			
			for(int MADLoop = 0; MADLoop < morphIndexes.Count; MADLoop++) {
				float morphPower = float.Parse(morphPowers[MADLoop]) * float.Parse(morphLevels[MADLoop]) * fadeOut;
				
				if ( !Mathf.Approximately( morphPower, 0.0f ) ) {
					MorphItem morphItem = new MorphItem ();
					morphItem.ShapeMorph = listToArray(morphShapesData[int.Parse(morphIndexes[MADLoop])]);									// section 0
					morphItem.ShapeLink = listToArray(morphShapesLinks[int.Parse(morphIndexes[MADLoop])]);									// section 1
					morphItem.ShapePower = morphPower;		// section 2

					morphArray.Add(morphItem);	
					meshChanged = 2;
				}
			}
			
			// Oh, if an animation is done, we take care of it here...
			if(removeanimation == true) {
				currentlyActiveAnimations.RemoveAt(CAALoop);  	// Watch this!  Make sure the for loop is actually looking at the length attribute or you could wander straight into null territory.
				CAALoop--; 
				meshChanged = 2;
				Frame = 0;
			}
		}
		
		// Well, were done with the array.  return it.
		return morphArray;
	}

	void morphAdd(string morphName, float startLevel, float endLevel, float timeFrame) {
		// Morph_Name: this is the name of the morph, as designated in Diff_Maps.Name
		// Start_Level: sets the starting morph level.  normally, you would use zero to start
		// End_Level: The morph level you want to end up at.
		// TimeFrame: How long to take to fade from start to end.
		
		// Keep in mind, even when the morph has finished timeframe, it's still morphing the mesh every frame until stopped by mm_Morph_Remove.
		// If you want to have a morph be added 'permanenty' to the mesh, use mm_SetShape_Set.
		// These work with mm_Animate_Play, allowing you to use morphs while animations are running.
		// This is good for things like randomized blinking, for example.
		
		// morph effects are additive, meaning that two morphs with overlapping effects do not average, they add to each other.
		
		int Found_morph = FindName( diffMaps, morphName );
		if ( Found_morph != -1) {
			if (timeFrame < 0.001) {
				// No such thing as zero time.  zero does not give useful data.
				timeFrame = 0.001f;
			}
			
			CurrentlyActiveMorph thisMorph = new CurrentlyActiveMorph();
			thisMorph.name 				= morphName;
			thisMorph.link				= Found_morph;
			thisMorph.CAMStartTime 		= Time.realtimeSinceStartup;
			thisMorph.CAMTimeFrame 		= timeFrame;
			thisMorph.CAMStartLevel 	= startLevel;
			thisMorph.CAMEndLevel 		= endLevel;
			thisMorph.CAMEnd 			= false;
			
			// Find the morph.
			int Found = FindName( currentlyActiveMorphs, morphName );
			if ( Found != -1) {
				// Found it.  Replace it!
				currentlyActiveMorphs[Found] = thisMorph;
			} else {
				//The morph does not exist.  Make it!
				currentlyActiveMorphs.Add(thisMorph);
			}
		} else {
			Report("ERROR: Morph not found" + morphName, 0);
		}
	}

	void morphRemove(string morphName, float timeFrame) {
		// Morph_Name: this is the name of the morph to stop effecting the mesh.
		// TimeFrame: is how long it takes for the morph to fade out.
		
		int Found = FindName( currentlyActiveMorphs, morphName );
		if (Found != -1) {
			// Found it.  Set it to fade and die.
			
			if (timeFrame < 0.001) {
				// No such thing as zero time.  zero does not give useful data.
				timeFrame = 0.001f;
			}
			
			float timeSpot = Mathf.InverseLerp (currentlyActiveMorphs [Found].CAMStartTime, 
			                                    currentlyActiveMorphs [Found].CAMStartTime + currentlyActiveMorphs [Found].CAMTimeFrame, 
			                                    Time.realtimeSinceStartup);
			float newStartLevel = (Mathf.Lerp (currentlyActiveMorphs [Found].CAMStartLevel, 
			                                    currentlyActiveMorphs [Found].CAMEndLevel, 
			                                    timeSpot));	
			
			// This line is checking data that later lines change.  DO THIS FIRST!
			currentlyActiveMorphs [Found].CAMStartLevel = newStartLevel; // what is the current morph level at this moment?  No need for error checking, since we know it exists...
			currentlyActiveMorphs [Found].CAMStartTime = Time.realtimeSinceStartup;
			currentlyActiveMorphs [Found].CAMTimeFrame = timeFrame;
			currentlyActiveMorphs [Found].CAMEndLevel = 0.0f;
			currentlyActiveMorphs [Found].CAMEnd = true;	
		}
	}

	List<float> morphLevel(string morphName) {
		// Morph_Name: this is the name of the morph you are questing information on.
		// If the returned array is null, then the morph is not active.  Otherwise, array[0] is a float of how much the morph is effecting the mesh.
		
		List<float> currentMorphLevel = new List<float> ();
		
		int Found = FindName( currentlyActiveMorphs, morphName );
		if ( Found != -1) {
			float Time_Spot = Mathf.InverseLerp(currentlyActiveMorphs[Found].CAMStartTime, 
			                                    currentlyActiveMorphs[Found].CAMStartTime + currentlyActiveMorphs[Found].CAMTimeFrame, 
			                                    Time.realtimeSinceStartup );
			currentMorphLevel.Add(Mathf.Lerp(currentlyActiveMorphs[Found].CAMStartLevel, 
			                                 currentlyActiveMorphs[Found].CAMEndLevel, 
			                                 Time_Spot ));
			return currentMorphLevel;
		}
		return null;
	}

	List<string> morphPlaying() {
		// Morph_Name: this is the name of the morph you are questing information on.
		
		// returns an array of all the morphs (not animations or Setshapes) effecting the mesh. 
		
		List<string> listing = new List<string> ();
		
		int listrange = currentlyActiveMorphs.Count;
		for (int listitem = 0 ; listitem < listrange ; listitem++) {
			listing.Add(currentlyActiveMorphs[listitem].name);
		}
		
		return listing;
	}

	void animatePlay(string morphAnimationName, float speedMultiple, int style) {
		// Morph_Animation_Name: The name of the animation you are starting from Animation_Recordings.Name
		// Speed_Multiple: This allows you to speed up and slow down an animation.  1.0 is normal.  Higher is faster.  Lower is slower.
		// Style: Animation_Style_End = 0, Animation_Style_Freeze = 1, Animation_Style_Loop 2, Animation_Style_PingPong = 3
		
		// These are NOT morphs, though they use Morph data.  They are datastreams of morph levels stored by frame.
		// They are use to play back complex animation from Blender 3d made with shapekeys.
		
		// Starts playing an animation.
		
		int Found_animation = FindName( animationRecordings, morphAnimationName );
		if ( Found_animation != -1) {
			var thisAnimation = new CurrentlyActiveAnimation();
			thisAnimation.name 				= morphAnimationName;
			thisAnimation.link				= Found_animation;
			thisAnimation.CAAStartTime 		= Time.realtimeSinceStartup;
			thisAnimation.CAAEndTime 		= -1;
			thisAnimation.CAAFadeTime 		= -1;
			thisAnimation.CAASpeed 			= speedMultiple;
			thisAnimation.CAAStyle 			= style;
			
			// Find the morph.
			int Found = FindName( currentlyActiveAnimations, morphAnimationName );
			if ( Found != -1) {
				// Found it.  Replace it!
				currentlyActiveAnimations[Found] = thisAnimation;
			} else {
				//The morph does not exist.  Make it!
				currentlyActiveAnimations.Add(thisAnimation);
			}
			
		} else {
			Report("ERROR: Morph Animation not found" + morphAnimationName, 0);
		}
	}


	void animateStop(string morphAnimationName, float easeOut) {
		// Morph_Animation_Name: Name of the animation to stop.
		// Ease_Out: How long to take easing out of the animation running.
		
		// stops an animation by fading it out.  To fade it out instantly, use and ease_out of zero.
		
		int Found = FindName( currentlyActiveAnimations, morphAnimationName );
		if ( Found != -1) {
			if (easeOut < 0.001) {
				// No such thing as zero time.  zero does not give useful data.
				easeOut = 0.001f;
			}
			
			// Found it.  Set it to fade and die.
			currentlyActiveAnimations[Found].CAAEndTime	= Time.realtimeSinceStartup;  // start fading from now...
			currentlyActiveAnimations[Found].CAAFadeTime	= Time.realtimeSinceStartup + easeOut;  // when we reach this time, remove the morph.
		}
	}


	List<int> animateFrame(string morphAnimationName) {
		// Morph_Animation_Name: Name of the animation to get the current frame from.
		// returns the current frame of the animation.
		
		// returns the current frame being played of the named animation.
		
		//var Current_Animation_Frame = new Array ();
		
		int Found = FindName( currentlyActiveAnimations, morphAnimationName);
		if (Found != -1) {
			int frame = Time2Frame( Time.realtimeSinceStartup - currentlyActiveAnimations[Found].CAAStartTime , currentlyActiveAnimations[Found].CAASpeed );
			List<int> ret = new List<int>();
			ret.Add (frame);
			return ret;
		} else {	
			return null;
		}
	}

	List<string> animatePlaying() {
		// returns a list of the names of currently playing animations.
		
		List<string> listing = new List<string> ();
		
		int listrange = currentlyActiveAnimations.Count;
		for (int listitem = 0 ; listitem < listrange ; listitem++) {
			listing.Add(currentlyActiveAnimations[listitem].name);
		}
		
		return listing;
	}

	void setShapeSet(string morphName, float morphLevel) {
		// Morph_Name: Name of the morph to apply.
		// Morph_Level: How much to apply it. 1.0 is fully.
		
		// With this, you can set a morph shape into the default mesh.
		// This means no FPS cost for it to be visible, but a large cost to set it.
		// Do NOT call this per frame.  Use the mm_Morph set of functions to animate getting to a shape, and then kill the morph while setting the shape.
		// It stacks, so each new shape added is added to all the previous ones.
		
		int Found = FindName( diffMaps, morphName );
		if ( Found != -1) {
			// group up the data for the morph into an morph item array.
			MorphItem morphItem = new MorphItem ();
			morphItem.ShapeMorph = listToArray(morphShapesData[Found]);	// section 0
			morphItem.ShapeLink = listToArray(morphShapesLinks[Found]);	// section 1
			morphItem.ShapePower = morphLevel;       					// section 2
			
			// And drop it into slot zero...
			List<MorphItem> morphArray = new List<MorphItem> ();
			morphArray.Add(morphItem); // slot 0
			
			mesh.vertices = morph(modMesh, morphArray);
			RecalculateNormals();
		}
	}

	void setShapeReset() {
		modMesh = baseMesh;
		mesh.vertices = baseMesh;
		RecalculateNormals ();
	}

	List<Vector3> arrayToList(Vector3[] vectorArray) {
		List<Vector3> output = new List<Vector3> ();
		for (int i = 0; i < vectorArray.Length; i++) {
			output.Add (vectorArray[i]);
		}
		return output;
	}

	List<string> arrayToList(string[] stringArray) {
		List<string> output = new List<string> ();
		for (int i = 0; i < stringArray.Length; i++) {
			output.Add (stringArray[i]);
		}
		return output;
	}

	Vector3[] listToArray(List<Vector3> vectorList) {
		Vector3[] output = new Vector3[vectorList.Count];
		for (int i = 0; i < vectorList.Count; i++) {
			output[i] = vectorList[i];
		}
		return output;
	}

	int[] listToArray(List<int> intList) {
		int[] output = new int[intList.Count];
		for (int i = 0; i < intList.Count; i++) {
			output[i] = intList[i];
		}
		return output;
	}

	Vector3[] morph(Vector3[] startingMesh, List<MorphItem> morphArray) {
		// The incoming variable is a special array, containing a lot of data.
		// Morph_Array [grouping number] 	[0] = morph array
		//										[1] = link array
		//										[2] = power level applied.
		
		Vector3[] workMesh = startingMesh;
		
		for (int morphItem = 0 ;morphItem < morphArray.Count ; morphItem++) {
			// okay, we are now going to apply each animation at the proper precentage to the model.
			MorphItem m = morphArray[morphItem];
			Vector3[] shapeMorph = m.ShapeMorph;
			int[] shapeLink = m.ShapeLink;
			float shapePower = m.ShapePower;
			
			for (int morphVerts = 0; morphVerts < shapeLink.Length; morphVerts++) {
				// In this case, we're only looping the vertices that MOVE.  All the rest are ignored, and this runs faster that way. 
				// You only pay for the parts you morph.
				workMesh[shapeLink[morphVerts]] += shapeMorph[morphVerts] * shapePower;
			}
		}
		// And here we have the final builtin array with the mesh shape we want!
		return workMesh;
	}

	void RecalculateNormals() {
		// The below recalulates the normal faces, but is not needed for most morphs.		
		if (metaMorphSettings.recalculateNormals == true) {
			mesh.RecalculateNormals();
		}
	}

	// simple function to translate seconds of animation to frames assuming 30 fps
	int Time2Frame( float timefromzero, float speedmultiplier) {
		return (int)Mathf.Round((timefromzero * speedmultiplier) * 30.0f);  // All Unity animations need to be at 30 fps.  bones, morphs, everything.  
	}

	void Report(string message, int level) {
		if (level <= metaMorphSettings.verbose) {
			Debug.Log (message);
		}
	}

	int FindName(List<CurrentlyActiveAnimation> searchableData, string requestedData) {
		int searchrange = searchableData.Count;
		for (int search = 0 ; search < searchrange ; search++) {
			if (requestedData == searchableData[search].name) {
				return search;
			}
		}
		return -1;
	}

	int FindName(List<CurrentlyActiveMorph> searchableData, string requestedData) {
		int searchrange = searchableData.Count;
		for (int search = 0 ; search < searchrange ; search++) {
			if (requestedData == searchableData[search].name) {
				return search;
			}
		}
		return -1;
	}

	int FindName(dataTemplate[] searchableData, string requestedData) {
		int searchrange = searchableData.Length;
		for (int search = 0 ; search < searchrange ; search++) {
			if (requestedData == searchableData[search].name) {
				return search;
			}
		}
		return -1;
	}
}
