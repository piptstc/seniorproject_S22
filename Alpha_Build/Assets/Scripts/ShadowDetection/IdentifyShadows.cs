using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using UnityEditor;
using System;

public class IdentifyShadows : MonoBehaviour
{
    [SerializeField]
    Camera cam;
    [SerializeField]
    int resizeAmount = 1;
    [SerializeField]
    GameObject[] labels = new GameObject[5];

    [SerializeField]
    int level = 0;
    float lenient;
    float basenum;
    float strictness;
    float brightThreshold;
    public bool holdingItem;

    List<Shadow> shadows;
    ShadowType currAvailShadow;
    Vector3 pastPos;

    void Start()
    {
        pastPos = this.gameObject.transform.position;
        lenient = (level == 2) ? 0.8f : 0.7f;
        basenum = (level == 1) ? 0.65f : 0.75f;
        strictness = (level == 2) ? 0.04f : 0.03f;
        strictness = (level == 1) ? 0.045f : strictness;
        brightThreshold = (level == 2) ? 2.5f : 2f;
        //basenum = 0.75f;

        holdingItem = false;
        DetectShadows();
        //Debug.Log("STARTED");
    }

    void Update()
    {
        if (holdingItem) return;
        float dist = Vector3.Distance(this.gameObject.transform.position, pastPos);
        //Debug.Log(dist);
        if (dist > 5)
        {
            //Debug.Log("-----------------------------------------------------------------------------------------");
            pastPos = this.gameObject.transform.position;
            RemoveLabels();
            DetectShadows();
        }
    }

    public void RemoveLabels()
    {
        GameObject labelList = GameObject.FindWithTag("labeler");
        foreach (Label lbl in labelList.GetComponents<Label>())
        {
            lbl.RemoveFromScene();
            Destroy(lbl);
        }
    }

    public void DetectShadows()
    {
        if (holdingItem) return;
        shadows = new List<Shadow>();
        RenderTexture inbetween = new RenderTexture(3 * Screen.width / 4, Screen.height, 24);
        Texture2D image = new Texture2D(3 * Screen.width / 4, Screen.height, TextureFormat.RGB24, false);
        Texture2D imageCheck = new Texture2D(3 * Screen.width / 4 / resizeAmount, Screen.height / resizeAmount, TextureFormat.RGBA32, false);
        Texture2D imageResized;

        //grab what the ortho cam sees and put it on a texture we can read
        if (cam.targetTexture != null) cam.targetTexture.Release();
        cam.targetTexture = inbetween;
        cam.Render();
        RenderTexture.active = inbetween;
        image.ReadPixels(new UnityEngine.Rect(0, 0, cam.targetTexture.width, cam.targetTexture.height), 0, 0);
        image.Apply();
        RenderTexture.active = null;

        imageResized = ResizeTexture(image, 3 * Screen.width / 4 / resizeAmount, Screen.height / resizeAmount);

        //int[] pixels = ApplyGreyscaleFilter(imageResized);
        //int[] pixels = ApplyDynamicGreyscaleFilter(imageResized);
        //int[] pixels = ApplyAdaptiveGreyscaleFilter(imageResized, 80, 0.83f);
        int[] pixels = ApplyContrastBasedGreyscaleFilter(imageResized, basenum);
        //Debug.Log(pixels.Length);

        //CreateImage(image.GetPixels(), "BW_ShadowBlobs");

        int maxOutContours = 40;
        int maxVertsPerContour = 40;
        CvVertex[] outContours = new CvVertex[800];
        int[] numVertsPerContour = new int[41];
        int numContours = 0;

        try
        {
            unsafe
            {
                fixed (int* _pixels = pixels, _numVertsPerContour = numVertsPerContour)
                {
                    fixed (CvVertex* _outContours = outContours)
                    {
                        //Debug.Log("stand in");
                        OpenCVInterop.DetectContours(_outContours, _numVertsPerContour, ref numContours, _pixels, imageResized.height, imageResized.width, maxOutContours, maxVertsPerContour, strictness);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Debug.Log("EXCEPTION: ");
            Debug.LogException(ex);
        }
        //Debug.Log("FOUND " + numContours + " SHADOWS");
        int startInd = 0;
        for (int i = 0; i < numContours; ++i)
        {
            if (i > maxOutContours) break;

            //Debug.Log("num points: " + numVertsPerContour[i]);
            if (numVertsPerContour[i] > 0 && numVertsPerContour[i] >= 2)
            {
                Vector2[] pointsOrdered = ReorderContourPoints(ref outContours, startInd, numVertsPerContour[i]);
                Vector2[] points = RemoveOutliers(ref pointsOrdered);
                if (numVertsPerContour[i] == 2 || IsConvex(ref points))
                {
                    Shadow thisShadow = new Shadow(ShadowType.unknown, image.width, image.height, points);
                    if (thisShadow.largestSpannedDist < 150 || numVertsPerContour[i] == 2)
                    {
                        thisShadow.Relabel(labels);
                        shadows.Add(thisShadow);
                    }
                    //else Debug.Log("too large, " + thisShadow.largestSpannedDist);
                }
                startInd += numVertsPerContour[i];
            }
        }


        /*Color[] pixelsOut = imageResized.GetPixels();
        for (int i = 0; i < numContours; ++i)
        {
            CvVertex[] pts = outContours;
            for (int j = 0; j < pts.Length; ++j)
            {
                CvVertex pt = pts[j];
                int ind = (int)pt.X + ((int)pt.Y) * imageCheck.width;
                if (ind < pixelsOut.Length)
                {
                    pixelsOut[ind].r = 1f;
                    pixelsOut[ind].g = 0.5f;
                    pixelsOut[ind].b = 0f;
                }
            }
        }

        for (int i = 0; i < shadows.Count; ++i)
        {
            for (int j = 0; j < shadows[i].contourPoints.Length; ++j)
            {
                Vector2 pt = shadows[i].contourPoints[j];
                int ind = (int)pt.x + ((int)pt.y) * imageCheck.width;
                if (ind < pixelsOut.Length)
                {
                    pixelsOut[ind].r = 0f;
                    pixelsOut[ind].g = 1f;
                    pixelsOut[ind].b = 1f;
                }
            }
        }
        CreateImage(pixelsOut, "ContourPoints");

        pixelsOut = GetImageReadyArray(pixels);
        for (int i = 0; i < numContours; ++i)
        {
            CvVertex[] pts = outContours;
            for (int j = 0; j < pts.Length; ++j)
            {
                CvVertex pt = pts[j];
                int ind = (int)pt.X + ((int)pt.Y) * imageCheck.width;
                if (ind < pixelsOut.Length)
                {
                    pixelsOut[ind].r = 1f;
                    pixelsOut[ind].g = 0.5f;
                    pixelsOut[ind].b = 0f;
                }
            }
        }

        for (int i = 0; i < shadows.Count; ++i)
        {
            for (int j = 0; j < shadows[i].contourPoints.Length; ++j)
            {
                Vector2 pt = shadows[i].contourPoints[j];
                int ind = (int)pt.x + ((int)pt.y) * imageCheck.width;
                if (ind < pixelsOut.Length)
                {
                    pixelsOut[ind].r = 0f;
                    pixelsOut[ind].g = 1f;
                    pixelsOut[ind].b = 1f;
                }
            }
        }
        CreateImage(pixelsOut, "ContourPoints_BW");*/
    }

    Vector2[] ReorderContourPoints(ref CvVertex[] contours, int startingInd, int size)
    {
        Vector2[] output = new Vector2[size];
        int x, y, xCenter = 0, yCenter = 0;
        for (int i = startingInd; i < startingInd + size; ++i)
        {
            x = (int)contours[i].X;
            y = (int)contours[i].Y;
            xCenter += x;
            yCenter += y;
            output[i - startingInd] = new Vector2(x, y);
        }
        xCenter /= size;
        yCenter /= size;

        output = output.OrderByDescending(point => Mathf.Atan2((float)point.x - xCenter, (float)point.y - yCenter)).ToArray();
        return output;
    }

    Vector2[] RemoveOutliers(ref Vector2[] points)
    {
        if (points.Length != 5 && points.Length != 6) return points;
        int outInd = -1;
        int closeInd = -1;
        int prev, next;
        double area, minArea, dist;
        minArea = GetPolygonalArea(ref points) / 30;
        for (int i = 0; i < points.Length; ++i)
        {
            prev = (i - 1) % points.Length;
            if (prev < 0) prev += points.Length;
            next = (i + 1) % points.Length;
            dist = Vector2.Distance(points[i], points[prev]);
            Debug.Log(dist);
            if (dist < 3) closeInd = i;
            area = (points[prev].x * points[i].y + points[i].x * points[next].y + points[next].x * points[prev].y - points[prev].y * points[i].x - points[i].y * points[next].x - points[next].y * points[prev].x) / 2.0;
            if (area < minArea)
            {
                Debug.Log(area + " and min " + minArea);
                int j = (i + 2) % points.Length;
                int k = (i - 2) % points.Length;
                if (k < 0) k += points.Length;
                double angleNext = Vector2.Angle(points[next] - points[i], points[next] - points[j]);
                //double angleprev = Vector2.Angle(points[prev] - points[i], points[prev] - points[k]);
                if (angleNext > 75 && angleNext < 105) outInd = prev;
                else outInd = next;
            }
        }

        if (outInd == -1 && closeInd == -1) return points;
        else
        {
            Vector2[] updatedPoints = new Vector2[points.Length - 1];
            int j = 0;
            for (int i = 0; i < points.Length; ++i)
            {
                if (i != outInd && i != closeInd) { updatedPoints[j] = points[i]; ++j; }

            }
            return updatedPoints;
        }
    }

    bool IsConvex(ref Vector2[] points)
    {
        //Debug.Log("SHADOW WITH " + points.Length + " POINTS, point0: " + points[0].x + ", " + points[0].y);
        int prev, next;
        float curr, last = 0f;
        for (int i = 0; i < points.Length; ++i)
        {
            //Debug.Log(points[i].x + ", " + points[i].y);
            prev = (i - 1) % points.Length;
            if (prev < 0) prev += points.Length;
            next = (i + 1) % points.Length;

            curr = ((points[i].x - points[prev].x) * (points[next].y - points[i].y)) - ((points[i].y - points[prev].y) * (points[next].x - points[i].x));
            //Debug.Log(curr + ", " + curr*last);
            if (curr * last < 0) return false;
            last = curr;
        }
        //Debug.Log("THIS ONE IS CONVEX!");
        return true;
    }

    float GetPolygonalArea(ref Vector2[] points)
    {
        float area = 0, add;
        int next;
        for (int i = 0; i < points.Length; ++i)
        {
            next = (i + 1) % points.Length;
            add = points[i].x * points[next].y - points[i].y * points[next].x;
            area += add;
        }
        return area;
    }

    Texture2D ResizeTexture(Texture2D input, int finalW, int finalH)
    {
        Texture2D toReturn = new Texture2D(finalW, finalH, TextureFormat.RGB24, false);
        for (int i = 0; i < finalW * finalH; i++)
        {
            int y = Mathf.FloorToInt(i / finalW);
            int x = i % finalW;
            toReturn.SetPixel(x, y, input.GetPixelBilinear(x * 1.0f / finalW * 1.0f, y * 1.0f / finalH * 1.0f));
        }
        toReturn.Apply();
        return toReturn;
    }

    int[] ApplyGreyscaleFilter(Texture2D image)
    {
        Color[] pixels = image.GetPixels();
        int[] toReturn = new int[pixels.Length];
        for (int i = 0; i < pixels.Length; i++)
        {
            if (pixels[i].r + pixels[i].g + pixels[i].b > .85)
            {
                toReturn[i] = 255;
            }
            else
            {
                toReturn[i] = 0;
            }
        }
        return toReturn;
    }

    int[] ApplyDynamicGreyscaleFilter(Texture2D image)
    {
        Color[] pixels = image.GetPixels();
        int[] toReturn = new int[pixels.Length];
        float[] tempSort = new float[pixels.Length];

        float threshold = 0;
        for (int i = 0; i < pixels.Length; i++)
        {
            tempSort[i] = (pixels[i].r + pixels[i].g + pixels[i].b);
        }

        Array.Sort(tempSort);
        threshold = tempSort[(toReturn.Length - 1) / 2] * 0.85f;
        Debug.Log("threshold: " + threshold);

        for (int i = 0; i < pixels.Length; i++)
        {
            if (pixels[i].r + pixels[i].g + pixels[i].b > threshold)
            {
                toReturn[i] = 0;
            }
            else
            {
                toReturn[i] = 255;
            }
        }
        return toReturn;
    }

    int[] ApplyAdaptiveGreyscaleFilter(Texture2D image, int windowSize, float thresholdConst)
    {
        Color[] pixels = image.GetPixels();
        int[] toReturn = new int[pixels.Length];
        float[,] integralImage = new float[image.width, image.height];

        float sum;
        for (int i = 0; i < image.width; ++i)
        {
            sum = 0;
            for (int j = 0; j < image.height; ++j)
            {
                sum += (image.GetPixel(i, j).r + image.GetPixel(i, j).g + image.GetPixel(i, j).b);
                if (i == 0) integralImage[i, j] = sum;
                else integralImage[i, j] = integralImage[i - 1, j] + sum;
            }
        }

        int x1, y1, x2, y2;
        float curr;
        for (int i = 0; i < image.width; ++i)
        {
            for (int j = 0; j < image.height; ++j)
            {
                x1 = Mathf.Max(0, i - windowSize / 2);
                y1 = Mathf.Max(0, j - windowSize / 2);
                x2 = Mathf.Min(image.width - 1, i + windowSize / 2);
                y2 = Mathf.Min(image.height - 1, j + windowSize / 2);
                sum = integralImage[x2, y2];
                curr = (image.GetPixel(i, j).r + image.GetPixel(i, j).g + image.GetPixel(i, j).b);
                if (y1 > 0) sum -= integralImage[x2, y1 - 1];
                if (x1 > 0) sum -= integralImage[x1 - 1, y2];
                if (x1 > 0 && y1 > 0) sum += integralImage[x1 - 1, y1 - 1];
                if (curr <= (sum / ((x2 - x1) * (y2 - y1))) * thresholdConst) toReturn[image.width * j + i] = 255;
                else toReturn[image.width * j + i] = 0;

            }
        }

        return toReturn;
    }

    int[] ApplyContrastBasedGreyscaleFilter(Texture2D image, float baseline)
    {
        Color[] pixels = image.GetPixels();
        int[] toReturn = new int[pixels.Length];
        int[] lastRow = new int[image.width];
        float[] lastVals = new float[image.width];
        int[] flags = new int[image.width];

        for (int i = 0; i < image.width; ++i)
        {
            lastRow[i] = 0;
            toReturn[i] = 0;
            flags[i] = 0;
            lastVals[i] = pixels[i].r + pixels[i].g + pixels[i].b;
        }

        float curr, last, last2;
        int count = 0;
        for (int i = image.width; i < pixels.Length; ++i)
        {
            curr = pixels[i].r + pixels[i].g + pixels[i].b;
            last = lastVals[count];
            last2 = (count > 0) ? lastVals[count - 1] : 0;
            //Debug.Log(curr);
            //if(curr/last < 1 || curr/last2 < 1) 
            //Debug.Log(curr + ", " + last + ", " + curr / last + " , " + curr / last2 + " , " + (curr - baseline) + " , " + (last / brightThreshold) + " , " + (last2 / brightThreshold));

            if (pixels[i].r > 0.98 && pixels[i].g < 0.02 && pixels[i].b > 0.9) //ANTISHADOW FLAG
            {
                toReturn[i] = 0;
                flags[count] = 1;
            }
            else if (curr / brightThreshold > 0.95)
            {
                //Debug.Log(curr + "too bright, threshold: " + brightThreshold);
                toReturn[i] = 0;
                lastVals[count] = curr;
                flags[count] = 0;
            }
            else if ((flags[count] == 1 || (count > 0 && flags[count - 1] == 1)) && curr / lastVals[count] < lenient)
            {
                toReturn[i] = 1;
                lastVals[count] = curr;
                flags[count] = 0;
            }
            else if (count > 0 && lastRow[count - 1] == 1 && Mathf.Abs(curr - last2) < 0.1)
            {
                toReturn[i] = 1;
                lastVals[count] = curr;
                flags[count] = 0;
            }
            else if (flags[count] != 1 && curr / last < 0.35 && (Mathf.Abs(curr - baseline) > 0.4 || (last / brightThreshold > 0.8)))
            {
                toReturn[i] = 1;
                lastVals[count] = curr;
                flags[count] = 0;
            }
            else if (flags[count] != 1 && curr / last < lenient && (Mathf.Abs(curr - baseline) > 0.6 || (last / brightThreshold > 0.6)))
            {
                toReturn[i] = 1;
                lastVals[count] = curr;
                flags[count] = 0;
            }
            else if (last / curr < .35)//&& (Mathf.Abs(last - baseline) > 0.4 || curr / brightThreshold > 0.8))
            {
                toReturn[i] = 0;
                lastVals[count] = curr;
                flags[count] = 0;
            }
            else if (last / curr < lenient)// && (Mathf.Abs(last - baseline) > 0.6 || curr / brightThreshold > 0.6))
            {
                toReturn[i] = 0;
                lastVals[count] = curr;
                flags[count] = 0;
            } //
            else if (count > 0 && flags[count - 1] != 1 && curr / last2 < 0.35 && (Mathf.Abs(curr - baseline) > 0.4 || (last2 / brightThreshold > 0.8)))
            {
                toReturn[i] = 1;
                lastVals[count] = curr;
                flags[count] = 0;
            }
            else if (count > 0 && flags[count - 1] != 1 && curr / last2 < lenient && (Mathf.Abs(curr - baseline) > 0.6 || (last2 / brightThreshold > 0.6)))
            {
                //Debug.Log("THIS ONE: " + curr/last2 + " , " + (curr-baseline) + " , " + (last2 / brightThreshold));
                toReturn[i] = 1;
                lastVals[count] = curr;
                flags[count] = 0;
            }
            else if (count > 0 && last2 / curr < .35)// && (Mathf.Abs(last2 - baseline) > 0.4 || curr / brightThreshold > 0.8))
            {
                toReturn[i] = 0;
                lastVals[count] = curr;
                flags[count] = 0;
            }
            else if (count > 0 && last2 / curr < lenient)// && (Mathf.Abs(last2 - baseline) > 0.6 || curr / brightThreshold > 0.6))
            {
                toReturn[i] = 0;
                lastVals[count] = curr;
                flags[count] = 0;
            } //
            else
            {
                toReturn[i] = lastRow[count];
                lastVals[count] = curr;
                flags[count] = 0;
            }

            //if(toReturn[i] == 1) Debug.Log(last / curr + " , " + last2 / curr + " , " + (last - baseline) + " , " + (curr / brightThreshold) );


            lastRow[count] = toReturn[i];
            if (++count >= image.width) count = 0;
        }

        return toReturn;
    }

    Color[] GetImageReadyArray(int[] inpixels)
    {
        Color[] pixels = new Color[inpixels.Length];

        for (int i = 0; i < pixels.Length; i++)
        {
            pixels[i] = new Color(inpixels[i], inpixels[i], inpixels[i]);
        }

        return pixels;
    }

    void CreateImage(Color[] pixels, string fileName)
    {
        Texture2D image = new Texture2D(3 * Screen.width / 4 / resizeAmount, Screen.height / resizeAmount);
        image.SetPixels(pixels);
        image.Apply();
        byte[] bytes = image.EncodeToPNG();
        string dirPath = Application.dataPath + "/./SaveImages/";
        File.WriteAllBytes(dirPath + fileName + ".png", bytes);
    }
}

internal static class OpenCVInterop
{
    [DllImport("openCVPlugin", EntryPoint = "DetectContours")]
    internal unsafe static extern void DetectContours(CvVertex* outContours, int* numVertsPerContour, ref int numContours, int* pixelsIn, int imageHeight, int imageWidth, int maxOutContours, int maxVertsPerContour, float strictness);
}

// Size =  byte size (2 ints = 4 bytes * 2 = 8 bytes)
[StructLayout(LayoutKind.Sequential, Size = 8)]
public struct CvVertex
{
    public int X, Y;
}